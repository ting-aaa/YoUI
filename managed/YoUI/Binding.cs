using System.ComponentModel;
using System.Windows.Input;

namespace YoUI;

/// <summary>A value owned by its creating thread. Dispatch background results to the UI thread before assigning it.</summary>
public sealed class ObservableValue<T>(T initial) : INotifyPropertyChanged
{
    private T value = initial;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    internal long Revision { get; private set; }
    public T Value
    {
        get => value;
        set
        {
            if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Observable values must be updated on their creating thread.");
            if (EqualityComparer<T>.Default.Equals(this.value, value)) return;
            this.value = value; Revision++; PropertyChanged?.Invoke(this, new(nameof(Value)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A same-thread command. Refresh after state affecting CanExecute changes.</summary>
public sealed class UiCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Func<object?, bool>? canExecute;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    public UiCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute); this.execute = execute; this.canExecute = canExecute;
    }
    private void Verify() { if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Commands must run on their creating thread."); }
    public bool CanExecute(object? parameter) { Verify(); return canExecute?.Invoke(parameter) ?? true; }
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() { Verify(); CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}

/// <summary>Owns explicit subscriptions independently of tree attachment. Dispose when the owning view exits.
/// Setup, notifications and disposal must stay on the creating thread. Nested updates are coalesced,
/// with a bounded retry count for application callbacks that continuously change their own source.</summary>
public sealed class BindingScope : IDisposable
{
    private readonly List<Subscription> cleanup = [];
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool disposed;
    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Dispatch binding operations to the creating UI thread.");
    }
    private void Verify() { VerifyThread(); ObjectDisposedException.ThrowIf(disposed, this); }
    private sealed class Subscription(BindingScope scope) : IDisposable
    {
        private readonly List<Action> releases = [];
        internal bool Disposed { get; private set; }
        internal void Add(Action release) => releases.Add(release);
        internal void Listen(Action subscribe, Action unsubscribe)
        {
            if (Disposed) return;
            releases.Add(unsubscribe);
            try { subscribe(); }
            finally
            {
                // A custom event accessor may call user code, dispose this scope,
                // then finish registering the listener. Remove that late registration too.
                if (Disposed) unsubscribe();
            }
        }
        public void Dispose()
        {
            scope.VerifyThread(); if (Disposed) return; Disposed = true;
            List<Exception>? errors = null;
            foreach (var release in releases.AsEnumerable().Reverse()) try { release(); } catch (Exception error) { (errors ??= []).Add(error); }
            releases.Clear(); scope.cleanup.Remove(this);
            if (errors != null) throw new AggregateException(errors);
        }
    }
    private IDisposable Create(Action<Subscription> setup)
    {
        Verify(); var subscription = new Subscription(this); cleanup.Add(subscription);
        try { setup(subscription); return subscription; }
        catch (Exception setupError)
        {
            try { subscription.Dispose(); }
            catch (Exception releaseError) { throw new AggregateException("Binding setup and rollback both failed.", setupError, releaseError); }
            throw;
        }
    }
    public IDisposable Track(Action release)
    {
        ArgumentNullException.ThrowIfNull(release); return Create(subscription => subscription.Add(release));
    }
    public IDisposable Observe<T>(INotifyPropertyChanged source, string property, Func<T> read, Action<T> apply)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(property); ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(apply);
        return Create(subscription =>
        {
            bool applying = false, pending = false;
            void Push()
            {
                if (subscription.Disposed) return; Verify(); pending = true;
                if (applying) return;
                applying = true;
                try
                {
                    int passes = 0;
                    while (pending && !subscription.Disposed)
                    {
                        if (++passes > 32) throw new InvalidOperationException("Binding callbacks did not settle after 32 updates.");
                        pending = false; var value = read(); if (!subscription.Disposed) apply(value);
                    }
                }
                finally { applying = false; }
            }
            void Changed(object? sender, PropertyChangedEventArgs e) { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == property) Push(); }
            // Register rollback before user-defined event accessors or the initial callback can fail.
            subscription.Listen(() => source.PropertyChanged += Changed, () => source.PropertyChanged -= Changed); Push();
        });
    }
    public IDisposable Bind<T>(ObservableValue<T> source, Action<T> apply) => Observe(source, nameof(source.Value), () => source.Value, apply);

    // The model stores the control's effective normalized value. A newer model write
    // made by an application callback takes priority over correction of an older write.
    private IDisposable TwoWay<T>(ObservableValue<T> source, Action<T> writeControl, Func<T> readControl,
        Action<Action<T>> subscribe, Action<Action<T>> unsubscribe, Func<T, string?>? validate = null, Action<string?>? validationChanged = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Create(subscription =>
        {
            bool busy = false, pending = false;
            void Push()
            {
                if (subscription.Disposed) return; Verify(); pending = true;
                if (busy) return;
                busy = true;
                try
                {
                    int passes = 0;
                    while (pending && !subscription.Disposed)
                    {
                        if (++passes > 32) throw new InvalidOperationException("Two-way binding callbacks did not settle after 32 updates.");
                        pending = false; var value = source.Value; long revision = source.Revision; writeControl(value);
                        if (subscription.Disposed) break;
                        if (revision != source.Revision) { pending = true; continue; }
                        var effective = readControl();
                        if (subscription.Disposed) break;
                        if (!EqualityComparer<T>.Default.Equals(value, effective))
                        {
                            source.Value = effective; pending = true; continue;
                        }
                        var error = validate?.Invoke(effective);
                        if (subscription.Disposed || revision != source.Revision) continue;
                        var validated = readControl();
                        if (!EqualityComparer<T>.Default.Equals(effective, validated))
                        {
                            source.Value = validated; pending = true; continue;
                        }
                        validationChanged?.Invoke(error);
                        if (!subscription.Disposed && revision == source.Revision)
                        {
                            var final = readControl();
                            if (!EqualityComparer<T>.Default.Equals(effective, final)) { source.Value = final; pending = true; }
                        }
                    }
                }
                finally { busy = false; }
            }
            void ControlChanged(T value)
            {
                if (subscription.Disposed) return; Verify(); if (busy) return;
                busy = true;
                try
                {
                    int passes = 0;
                    while (!subscription.Disposed)
                    {
                        if (++passes > 32) throw new InvalidOperationException("Control validation callbacks did not settle after 32 updates.");
                        // Earlier observers may already have corrected this event's value.
                        value = readControl(); long revision = source.Revision;
                        var error = validate?.Invoke(value);
                        if (subscription.Disposed) break;
                        if (revision != source.Revision) { pending = true; break; }
                        if (!EqualityComparer<T>.Default.Equals(value, readControl())) continue;
                        validationChanged?.Invoke(error);
                        if (subscription.Disposed) break;
                        if (revision != source.Revision) { pending = true; break; }
                        if (!EqualityComparer<T>.Default.Equals(value, readControl())) continue;
                        if (error == null) source.Value = value;
                        break;
                    }
                }
                finally { busy = false; }
                if (pending && !subscription.Disposed) Push();
            }
            void ModelChanged(object? sender, PropertyChangedEventArgs e) { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(source.Value)) Push(); }
            subscription.Listen(() => source.PropertyChanged += ModelChanged, () => source.PropertyChanged -= ModelChanged);
            subscription.Listen(() => subscribe(ControlChanged), () => unsubscribe(ControlChanged)); Push();
        });
    }
    public IDisposable BindText(TextBox control, ObservableValue<string> source, Func<string, string?>? validate = null, Action<string?>? validationChanged = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TwoWay(source, value => control.Text = value, () => control.Text, handler => control.Changed += handler, handler => control.Changed -= handler, validate, validationChanged);
    }
    public IDisposable BindToggle(Toggle control, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TwoWay(source, value => control.Value = value, () => control.Value, handler => control.Changed += handler, handler => control.Changed -= handler);
    }
    public IDisposable BindToggle(CheckBox control, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TwoWay(source, value => control.Value = value, () => control.Value, handler => control.Changed += handler, handler => control.Changed -= handler);
    }
    public IDisposable BindNumber(NumericUpDown control, ObservableValue<float> source)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TwoWay(source, value => control.Value = value, () => control.Value, handler => control.ValueChanged += handler, handler => control.ValueChanged -= handler);
    }
    public IDisposable BindSelection(ComboBox control, ObservableValue<int> source)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TwoWay(source, value => control.SelectedIndex = value, () => control.SelectedIndex, handler => control.SelectionChanged += handler, handler => control.SelectionChanged -= handler);
    }
    public IDisposable BindSelection(VirtualList control, ObservableValue<int> source)
    {
        ArgumentNullException.ThrowIfNull(control);
        return TwoWay(source, value => control.SelectedIndex = value, () => control.SelectedIndex, handler => control.SelectionChanged += handler, handler => control.SelectionChanged -= handler);
    }
    public IDisposable BindCommand(Button control, ICommand command, Func<object?>? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(control); ArgumentNullException.ThrowIfNull(command);
        return Create(subscription =>
        {
            bool refreshing = false, pending = false;
            void Refresh(object? sender, EventArgs e)
            {
                if (subscription.Disposed) return; Verify(); pending = true; if (refreshing) return;
                refreshing = true;
                try
                {
                    int passes = 0;
                    while (pending && !subscription.Disposed)
                    {
                        if (++passes > 32) throw new InvalidOperationException("Command availability did not settle after 32 updates.");
                        pending = false; var value = parameter?.Invoke(); if (subscription.Disposed) break;
                        bool enabled = command.CanExecute(value); if (!subscription.Disposed) control.Enabled = enabled;
                    }
                }
                finally { refreshing = false; }
            }
            void Click()
            {
                if (subscription.Disposed) return; Verify(); var value = parameter?.Invoke();
                if (!subscription.Disposed && command.CanExecute(value) && !subscription.Disposed) command.Execute(value);
            }
            subscription.Listen(() => command.CanExecuteChanged += Refresh, () => command.CanExecuteChanged -= Refresh);
            subscription.Listen(() => control.Clicked += Click, () => control.Clicked -= Click); Refresh(null, EventArgs.Empty);
        });
    }
    public void Dispose()
    {
        VerifyThread(); if (disposed) return; disposed = true;
        List<Exception>? errors = null;
        foreach (var subscription in cleanup.ToArray().Reverse()) try { subscription.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        cleanup.Clear(); if (errors != null) throw new AggregateException(errors);
    }
}
