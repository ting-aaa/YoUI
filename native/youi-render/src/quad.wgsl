struct Globals { size: vec2<f32>, padding: vec2<f32> }
@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var atlas: texture_2d<f32>;
@group(0) @binding(2) var texSampler: sampler;
struct In {
 @location(0) rect: vec4<f32>, @location(1) color: vec4<f32>,
 @location(2) color2: vec4<f32>, @location(3) uv: vec4<f32>,
 @location(4) clip: vec4<f32>, @location(5) params: vec4<f32>
}
struct Out {
 @builtin(position) position: vec4<f32>, @location(0) local: vec2<f32>,
 @location(1) @interpolate(flat) rect: vec4<f32>, @location(2) @interpolate(flat) color: vec4<f32>,
 @location(3) @interpolate(flat) color2: vec4<f32>, @location(4) uv: vec2<f32>,
 @location(5) @interpolate(flat) clip: vec4<f32>, @location(6) @interpolate(flat) params: vec4<f32>
}
@vertex fn vs(input: In,@builtin(vertex_index) index:u32)->Out {
 let corners=array<vec2<f32>,6>(vec2(0.,0.),vec2(1.,0.),vec2(0.,1.),vec2(0.,1.),vec2(1.,0.),vec2(1.,1.));
 let p=corners[index]; var pixel=input.rect.xy+p*input.rect.zw;
 if input.params.y>2.5 {
   let points=array<vec2<f32>,6>(input.rect.xy,input.rect.zw,vec2(input.params.x,input.params.z),input.rect.xy,input.rect.xy,input.rect.xy);
   pixel=points[index];
 }
 var o:Out; o.position=vec4(pixel.x/globals.size.x*2.-1.,1.-pixel.y/globals.size.y*2.,0.,1.);
 o.local=p*input.rect.zw; o.rect=input.rect;o.color=input.color;o.color2=input.color2;
 o.uv=input.uv.xy+p*input.uv.zw;o.clip=input.clip;o.params=input.params;return o;
}
fn linear(c:vec3<f32>)->vec3<f32> {return select(pow((c+vec3(0.055))/1.055,vec3(2.4)),c/12.92,c<=vec3(0.04045));}
@fragment fn fs(i:Out)->@location(0) vec4<f32> {
 let pixel=i.position.xy;
 if any(pixel<i.clip.xy) || any(pixel>=i.clip.xy+i.clip.zw) {discard;}
 if i.params.y>2.5 {return vec4(linear(i.color.rgb)*i.color.a,i.color.a);}
 if i.params.y>1.5 {return textureSample(atlas,texSampler,i.uv)*i.color.a;}
 let t=clamp(i.local.y/max(i.rect.w,1.),0.,1.);
 var color=mix(vec4(linear(i.color.rgb),i.color.a),vec4(linear(i.color2.rgb),i.color2.a),t);
 if i.params.y>0.5 {
   let tex=textureSample(atlas,texSampler,i.uv);color=vec4(color.rgb*tex.rgb,color.a*tex.a);
 } else {
   let r=min(i.params.x,min(i.rect.z,i.rect.w)*0.5);
   let q=abs(i.local-i.rect.zw*0.5)-(i.rect.zw*0.5-vec2(r));
   let d=length(max(q,vec2(0.)))+min(max(q.x,q.y),0.)-r;
   // Distances are already in physical pixels. Derivatives of the min/max SDF
   // differ at quad corners across APIs and can fade opaque rectangle pixels.
   color.a*=1.-smoothstep(-0.5,0.5,d);
 }
 return vec4(color.rgb*color.a,color.a);
}
