const __vite__mapDeps=(i,m=__vite__mapDeps,d=(m.f||(m.f=["assets/MapboxTerrain-ChnLijVM.js","assets/vendor-react-CpJAkXKM.js","assets/vendor-three-BjkEcI3n.js","assets/vendor-mapbox-terrain-Bg-QaeT0.js"])))=>i.map(i=>d[i]);
import{_ as ae}from"./vendor-geo-Blh7LOn-.js";import{r as i,j as o,R as X}from"./vendor-react-CpJAkXKM.js";import{u as Z,b as le,C as ue,T as ce,G as pe,a as de,O as fe}from"./vendor-r3f-CDJVkdB8.js";import{ak as me,M as he,i as U,n as K,ac as _,X as Q,D as $,al as J,J as ge,d as V,a6 as Ce,a9 as ve,y as Y,Q as ee,E as xe,c as be,O as ye}from"./vendor-three-BjkEcI3n.js";import{g as q}from"./vendor-utils-xgxdCp6f.js";import{h as we,D as te,d as oe,e as Se,s as Ee}from"./index-CjWYPuTt.js";import{P as Me,h as je,c as ze,d as Pe,b as Te,e as De,E as ke,a as Le,S as Ae,M as Ne,f as He,T as Fe,g as Ve,C as Ie,k as Re}from"./ProjectionController-DpqT-eQ1.js";const ne=`
#include <clipping_planes_pars_vertex>

attribute vec3 color;
attribute float intensity;
attribute float relHeight; // NEW

varying vec3 vColor;
varying float vIntensity;
varying vec3 vNormal;
varying vec3 vModelNormal; // NEW (model space normála pre segmenter)
varying vec3 vViewPosition;
varying float vWorldZ;
varying float vRelHeight; // NEW

uniform float pointSize;

void main() {
    vColor = color;
    vIntensity = intensity;
    vRelHeight = relHeight; // NEW
    vWorldZ = position.y; // In v2, Y is altitude
    vModelNormal = length(normal) > 0.001 ? normal : vec3(0.0); // NEW
    
    // Transform normal to view space (pre headlight lighting)
    vec3 rawViewNormal = normalMatrix * normal;
    vNormal = length(rawViewNormal) > 0.001 ? normalize(rawViewNormal) : vec3(0.0);

    vec4 mvPosition = modelViewMatrix * vec4(position, 1.0);
    gl_Position = projectionMatrix * mvPosition;

    // Point size attenuation
    gl_PointSize = pointSize * (300.0 / -mvPosition.z);
    gl_PointSize = clamp(gl_PointSize, 1.0, 32.0);

    vViewPosition = - mvPosition.xyz;
    #include <clipping_planes_vertex>
}
`,re=`
#include <clipping_planes_pars_fragment>

varying vec3 vColor;
varying float vIntensity;
varying vec3 vNormal;
varying vec3 vModelNormal; // NEW (model space normála)
varying vec3 vViewPosition;
varying float vWorldZ;
varying float vRelHeight; // NEW

uniform float brightness;
uniform float plasticity;
uniform int colorMode; // 0: original, 1: elevation, 2: natural
uniform vec3 customColor;
uniform vec3 highlightColor;
uniform float minZ;
uniform float maxZ;
uniform int uHasUsableVertexColors;
uniform int pointShape; // 0 square, 1 sphere, 2 rounded diamond, 3 hex, 4 surfel
uniform int uEnableEDL;
uniform float uEdlStrength;

// Real-time GPU segmenter uniforms
uniform int uViewMode; // 0: All, 1: Floor, 2: Ceiling, 3: Contour Floor, 4: Heatmap Floor
uniform float uHeightThreshold; // threshold for relHeight (0.0 to 1.0)
uniform float uAngleThreshold;  // threshold for normal.y (0.0 to 1.0)

float getPointShapeAlpha(vec2 coord) {
    if (pointShape == 0) {
        return 1.0;
    }

    if (pointShape == 1) {
        float circleDist = dot(coord, coord);
        return 1.0 - smoothstep(0.92, 1.0, circleDist);
    }

    if (pointShape == 2) {
        vec2 diamondCoord = vec2(
            coord.x * 0.82 + coord.y * 0.32,
            -coord.x * 0.32 + coord.y * 0.82
        );
        float diamondDist = abs(diamondCoord.x) + abs(diamondCoord.y);
        return 1.0 - smoothstep(0.92, 1.02, diamondDist);
    }

    if (pointShape == 3) {
        vec2 hexCoord = abs(coord);
        float hexDist = max(hexCoord.x * 0.8660254 + hexCoord.y * 0.5, hexCoord.y);
        return 1.0 - smoothstep(0.88, 0.98, hexDist);
    }

    if (pointShape == 4) {
        // Surfel (Continuous disk splatting with smooth border)
        float circleDist = dot(coord, coord);
        if (circleDist > 1.0) return 0.0;
        return 1.0 - smoothstep(0.75, 1.0, circleDist);
    }

    return 1.0;
}

float getPointEdgeFactor(vec2 coord) {
    if (pointShape == 0) {
        // Square border (crisp dark edge frame)
        float sq = max(abs(coord.x), abs(coord.y));
        return smoothstep(0.65, 0.94, sq);
    }
    if (pointShape == 1) {
        // Sphere/Circle border (crisp dark ring)
        float d = dot(coord, coord);
        return smoothstep(0.48, 0.92, d);
    }
    if (pointShape == 2) {
        // Diamond border
        vec2 diamondCoord = vec2(
            coord.x * 0.82 + coord.y * 0.32,
            -coord.x * 0.32 + coord.y * 0.82
        );
        float d = abs(diamondCoord.x) + abs(diamondCoord.y);
        return smoothstep(0.62, 0.93, d);
    }
    if (pointShape == 3) {
        // Hexagon border
        vec2 hexCoord = abs(coord);
        float d = max(hexCoord.x * 0.8660254 + hexCoord.y * 0.5, hexCoord.y);
        return smoothstep(0.62, 0.92, d);
    }
    if (pointShape == 4) {
        // Surfel disk edge ring
        float d = dot(coord, coord);
        return smoothstep(0.45, 0.88, d);
    }
    return 0.0;
}

vec3 getElevationColor(float z) {
    float t = clamp((z - minZ) / (maxZ - minZ), 0.0, 1.0);
    
    // Gradient stops (Blue -> Cyan -> Green -> Yellow -> Red)
    vec3 c0 = vec3(0.08, 0.18, 0.65);
    vec3 c1 = vec3(0.10, 0.48, 0.85);
    vec3 c2 = vec3(0.12, 0.78, 0.72);
    vec3 c3 = vec3(0.18, 0.87, 0.38);
    vec3 c4 = vec3(0.80, 0.94, 0.10);
    vec3 c5 = vec3(0.97, 0.60, 0.05);
    vec3 c6 = vec3(0.88, 0.10, 0.10);
    
    if (t < 0.18) return mix(c0, c1, t / 0.18);
    if (t < 0.35) return mix(c1, c2, (t - 0.18) / (0.35 - 0.18));
    if (t < 0.50) return mix(c2, c3, (t - 0.35) / (0.50 - 0.35));
    if (t < 0.65) return mix(c3, c4, (t - 0.50) / (0.65 - 0.50));
    if (t < 0.80) return mix(c4, c5, (t - 0.65) / (0.80 - 0.65));
    return mix(c5, c6, (t - 0.80) / (1.0 - 0.80));
}

void main() {
    vec2 spriteCoord = gl_PointCoord * 2.0 - 1.0;
    float shapeAlpha = getPointShapeAlpha(spriteCoord);
    if (shapeAlpha <= 0.02) discard;

    // 0. Manual Clipping with Highlight Logic
    float minClipDist = 10000.0;
    bool hasClip = false;

    #if NUM_CLIPPING_PLANES > 0
    for ( int i = 0; i < NUM_CLIPPING_PLANES; i ++ ) {
        vec4 plane = clippingPlanes[ i ];
        // Standard Three.js distance calculation in view space
        float dist = dot(vViewPosition, plane.xyz) - plane.w;
        
        if ( dist > 0.0 ) discard; // Standard clipping discard
        
        minClipDist = min(minClipDist, abs(dist));
        hasClip = true;
    }
    #endif

    // ─── Speleo Segmenter: Mold Parting Line Logic (GPU) ─────────────────────
    // Zistíme, či LiDAR model má vypočítané normály. 
    bool hasNormals = (length(vModelNormal) > 0.1);
    vec3 mNormal = hasNormals ? normalize(vModelNormal) : vec3(0.0);

    // Podlaha: body ležiace pod deliacou rovinou (uHeightThreshold)
    // Sklon nahor (mNormal.y > uAngleThreshold) odstraňuje strmé steny.
    bool isFloor = (vRelHeight < uHeightThreshold) && (!hasNormals || mNormal.y > uAngleThreshold);
    
    // Strop: body ležiace nad deliacou rovinou (uHeightThreshold)
    // Sklon nadol (mNormal.y < -uAngleThreshold) odstraňuje strmé steny.
    bool isCeiling = (vRelHeight > uHeightThreshold) && (!hasNormals || mNormal.y < -uAngleThreshold);

    if (uViewMode == 1) { // Floor only
        if (!isFloor) discard;
    } else if (uViewMode == 2) { // Ceiling only
        if (!isCeiling) discard;
    } else if (uViewMode == 3 || uViewMode == 4) { // Contour/Heatmap floor modes
        if (!isFloor) discard;
    }

    // 1. Base color selection
    vec3 baseColor;
    if (uViewMode == 4) {
        // Heatmap floor: height elevation coloring
        baseColor = getElevationColor(vWorldZ);
    } else if (uViewMode == 3) {
        // Contour floor: Draw elegant 1m contours on dark slate floor
        vec3 terrainColor = vec3(0.15, 0.20, 0.25); // Slate background
        
        float fractZ = fract(vWorldZ + 0.5) - 0.5;
        float distToContour = abs(fractZ);
        float contourWidth = 0.03; // sharp 3cm lines
        
        // Anti-aliased lines using smoothstep
        float contourIntensity = 1.0 - smoothstep(0.0, contourWidth, distToContour);
        
        // Highlight every 5th meter contour
        float isIndexContour = 1.0 - smoothstep(0.0, contourWidth * 1.5, abs(fract(vWorldZ / 5.0 + 0.5) - 0.5) * 5.0);
        
        vec3 mainContourCol = vec3(0.95, 0.75, 0.15); // Golden main contours
        vec3 subContourCol = vec3(0.65, 0.70, 0.75);  // Light silver sub-contours
        vec3 finalContourColor = mix(subContourCol, mainContourCol, isIndexContour);
        
        baseColor = mix(terrainColor, finalContourColor, contourIntensity);
    } else if (colorMode == 1) {
        baseColor = getElevationColor(vWorldZ);
    } else if (colorMode == 2) {
        baseColor = customColor; 
    } else {
        // Original mode: trust vertex colors only when the source declared real color data.
        if (uHasUsableVertexColors == 1) {
            baseColor = vColor;
        } else {
            baseColor = customColor;
        }
    }
    
    // 2. Lighting calculation (Headlight effect)
    vec3 lightDir = normalize(vec3(0.2, 0.2, 1.0));
    float dotNL = dot(vNormal, lightDir);
    float spriteShade = 1.0;
    float spriteDiffuse = 0.5;
    if (pointShape == 1) {
        float spriteDist = dot(spriteCoord, spriteCoord);
        float spriteDepth = sqrt(max(0.0, 1.0 - spriteDist));
        spriteShade = mix(0.72, 1.12, spriteDepth);
        vec3 pointSpriteNormal = normalize(vec3(spriteCoord.x * 0.45, -spriteCoord.y * 0.45, spriteDepth));
        spriteDiffuse = max(dot(pointSpriteNormal, lightDir), 0.0);
    }
    float diffuse = (length(vNormal) > 0.01) ? max(dotNL, 0.0) : spriteDiffuse;
    
    // 3. Intensity influence
    float baseIntensityEffect = 0.4 + vIntensity * 0.6;
    float brightIntensity = mix(1.0, baseIntensityEffect, plasticity);
    
    // 4. Final Shading
    float ambient = 0.4;
    float light = (ambient + (diffuse * 0.6 * plasticity)) * brightness;
    
    vec3 finalColor = baseColor * light * brightIntensity * spriteShade;
    finalColor *= mix(0.94, 1.03, shapeAlpha);
    
    // 5. Gamma correction
    finalColor = pow(finalColor, vec3(0.8 / clamp(brightness, 0.5, 2.0)));

    // 6. Point Silhouette / EDL Outline Rim (instant thin crisp black edge around disks/squares)
    float edgeFactor = getPointEdgeFactor(spriteCoord);
    if (uEnableEDL == 1) {
        float borderDarken = 0.75 * clamp(uEdlStrength, 0.5, 2.0);
        finalColor = mix(finalColor, vec3(0.01, 0.01, 0.02), edgeFactor * borderDarken);
    }

    // 7. Highlight for Clipping Edges
    if (hasClip && minClipDist < 0.15) {
        float highlightStrength = 1.0 - (minClipDist / 0.15);
        finalColor = mix(finalColor, highlightColor, highlightStrength * 0.9);
    }

    gl_FragColor = vec4(finalColor, 1.0);
}
`,Oe=({url:t,pointSize:e=1,brightness:d=1,plasticity:f=1,pointShape:E=te,colorMode:P="original",customColor:m="#ffffff",highlightColor:v="#ff4444",minZ:C=-100,maxZ:z=100,clippingPlanes:h=[],viewMode:p="all",heightThreshold:T=.4,angleThreshold:A=.5,enableEDL:x=!1,edlStrength:b=1})=>{const[M,N]=i.useState(new Map),{camera:D}=Z(),s=i.useRef(null),u=i.useRef(new Map),w=i.useRef(new me),g=i.useRef(new he),r=P==="elevation"?1:P==="natural"?2:0,S=p==="floor"?1:p==="ceiling"?2:p==="contour"?3:p==="heatmap"?4:0,F=oe(E),I=i.useMemo(()=>new U(m),[m]),H=i.useMemo(()=>new U(v),[v]);return i.useEffect(()=>{let k=!0;const l=new AbortController,O=new Worker(new URL("/dankez-caveview/v2.5.15-slx.2/assets/pointcloud.worker-CM9KUmIQ.js",import.meta.url),{type:"module"});return s.current=O,O.onmessage=j=>{const y=j.data;if(y.type==="POINTCLOUD_CHUNK"&&y.points&&y.colors&&y.normals&&y.intensity&&y.relHeight&&y.bounds&&y.id){const n=new K;n.setAttribute("position",new _(y.points,3)),n.setAttribute("color",new _(y.colors,3)),n.setAttribute("normal",new _(y.normals,3)),n.setAttribute("intensity",new _(y.intensity,1)),n.setAttribute("relHeight",new _(y.relHeight,1)),n.computeBoundingSphere();const a=new Q({uniforms:{pointSize:{value:e},brightness:{value:d},plasticity:{value:f},pointShape:{value:F},colorMode:{value:r},customColor:{value:I},highlightColor:{value:H},minZ:{value:C},maxZ:{value:z},uHasUsableVertexColors:{value:we(y.colors,y.vertexCount||0)?1:0},uViewMode:{value:S},uHeightThreshold:{value:T},uAngleThreshold:{value:A},uEnableEDL:{value:x?1:0},uEdlStrength:{value:b}},vertexShader:ne,fragmentShader:re,transparent:!1,depthWrite:!0,depthTest:!0,clipping:!0,clippingPlanes:h,side:$}),c=new J(n,a);c.frustumCulled=!1;const L=new ge(new V(y.bounds.min.x,y.bounds.min.y,y.bounds.min.z),new V(y.bounds.max.x,y.bounds.max.y,y.bounds.max.z)),R={points:c,bounds:L};u.current.set(y.id,R),N(B=>{const W=new Map(B);return W.set(y.id,R),W})}},fetch(t,{signal:l.signal}).then(j=>j.arrayBuffer()).then(j=>{k&&O.postMessage({buffer:j},[j])}).catch(j=>{j&&typeof j=="object"&&"name"in j&&j.name==="AbortError"||console.error("Failed to fetch pointcloud:",j)}),()=>{k=!1,l.abort(),O.terminate(),u.current.forEach(j=>{j.points.geometry.dispose(),Array.isArray(j.points.material)?j.points.material.forEach(y=>y.dispose()):j.points.material.dispose()}),u.current.clear(),N(new Map)}},[t]),i.useEffect(()=>{M.forEach(k=>{const l=k.points.material;l.uniforms&&(l.uniforms.pointSize&&(l.uniforms.pointSize.value=e),l.uniforms.brightness&&(l.uniforms.brightness.value=d),l.uniforms.plasticity&&(l.uniforms.plasticity.value=f),l.uniforms.pointShape&&(l.uniforms.pointShape.value=F),l.uniforms.colorMode&&(l.uniforms.colorMode.value=r),l.uniforms.customColor&&(l.uniforms.customColor.value=I),l.uniforms.highlightColor&&(l.uniforms.highlightColor.value=H),l.uniforms.minZ&&(l.uniforms.minZ.value=C),l.uniforms.maxZ&&(l.uniforms.maxZ.value=z),l.uniforms.uViewMode&&(l.uniforms.uViewMode.value=S),l.uniforms.uHeightThreshold&&(l.uniforms.uHeightThreshold.value=T),l.uniforms.uAngleThreshold&&(l.uniforms.uAngleThreshold.value=A),l.uniforms.uEnableEDL&&(l.uniforms.uEnableEDL.value=x?1:0),l.uniforms.uEdlStrength&&(l.uniforms.uEdlStrength.value=b)),l.clippingPlanes=h,l.needsUpdate=!0})},[e,d,f,F,P,m,v,I,H,C,z,h,M,p,T,A,x,b]),le(()=>{const k=w.current,l=g.current;l.multiplyMatrices(D.projectionMatrix,D.matrixWorldInverse),k.setFromProjectionMatrix(l),M.forEach(O=>{O.points.visible=k.intersectsBox(O.bounds)})}),o.jsx("group",{children:Array.from(M.values()).map((k,l)=>o.jsx("primitive",{object:k.points},l))})};function _e(t){const e=t.points,d=t.pointCount,f=new Float32Array(d);if(!e||d===0)return f;const P=1/Math.max(.2,Math.max(t.bounds.size.x,t.bounds.size.y,1)/280),m=new Map;for(let v=0;v<d;v++){const C=v*3,z=`${Math.floor(e[C]*P)},${Math.floor(e[C+1]*P)}`,h=e[C+2],p=m.get(z);p?(h<p.min&&(p.min=h),h>p.max&&(p.max=h)):m.set(z,{min:h,max:h})}for(let v=0;v<d;v++){const C=v*3,z=`${Math.floor(e[C]*P)},${Math.floor(e[C+1]*P)}`,h=m.get(z);if(!h){f[v]=0;continue}const p=(h.max-h.min)/2;f[v]=p>.02?(e[C+2]-(h.min+h.max)/2)/p:-1}return f}function Be(t){const e=t.pointCount;return!t.pointColors||t.pointColors.length<e*3?!1:Se(t,e)}function We(t){const e=t.pointCount;return!(!t.pointNormals||t.pointNormals.length<e*3||t.hasPointNormals===!1)}const Ue=({cave:t,pointSize:e=1,brightness:d=1,plasticity:f=1,pointShape:E=te,colorMode:P="original",customColor:m="#ffffff",highlightColor:v="#ff4444",minZ:C=-100,maxZ:z=100,clippingPlanes:h=[],viewMode:p="all",heightThreshold:T=.4,angleThreshold:A=.5,enableEDL:x=!1,edlStrength:b=1})=>{const M=P==="elevation"?1:P==="natural"?2:0,N=p==="floor"?1:p==="ceiling"?2:p==="contour"?3:p==="heatmap"?4:0,D=oe(E),s=i.useMemo(()=>new U(m),[m]),u=i.useMemo(()=>new U(v),[v]),w=i.useMemo(()=>Be(t),[t]),g=i.useMemo(()=>{const S=t.pointCount,F=t.points;if(!F||S===0)return null;const I=new Float32Array(S*3),H=new Float32Array(S*3),k=We(t)?new Float32Array(S*3):null,l=new Float32Array(S),O=_e(t),j=w&&!!t.pointColors&&t.pointColors.length>=S*3,y=!!t.pointIntensity&&t.pointIntensity.length>=S;for(let a=0;a<S;a++){const c=a*3;I[c]=F[c],I[c+1]=F[c+2],I[c+2]=-F[c+1],j?(H[c]=t.pointColors[c],H[c+1]=t.pointColors[c+1],H[c+2]=t.pointColors[c+2]):(H[c]=0,H[c+1]=0,H[c+2]=0),k&&(k[c]=t.pointNormals[c],k[c+1]=t.pointNormals[c+2],k[c+2]=-t.pointNormals[c+1]),l[a]=y?t.pointIntensity[a]:1}const n=new K;return n.setAttribute("position",new _(I,3)),n.setAttribute("color",new _(H,3)),k&&n.setAttribute("normal",new _(k,3)),n.setAttribute("intensity",new _(l,1)),n.setAttribute("relHeight",new _(O,1)),n.computeBoundingSphere(),n},[t,t.pointNormals,w]),r=i.useMemo(()=>new Q({uniforms:{pointSize:{value:e},brightness:{value:d},plasticity:{value:f},pointShape:{value:D},colorMode:{value:M},customColor:{value:s},highlightColor:{value:u},minZ:{value:C},maxZ:{value:z},uHasUsableVertexColors:{value:w?1:0},uViewMode:{value:N},uHeightThreshold:{value:T},uAngleThreshold:{value:A},uEnableEDL:{value:x?1:0},uEdlStrength:{value:b}},vertexShader:ne,fragmentShader:re,transparent:!1,depthWrite:!0,depthTest:!0,clipping:!0,clippingPlanes:h,side:$}),[]);return i.useEffect(()=>{if(g)return()=>{g.dispose()}},[g]),i.useEffect(()=>()=>{r.dispose()},[r]),i.useEffect(()=>{r.uniforms.pointSize.value=e,r.uniforms.brightness.value=d,r.uniforms.plasticity.value=f,r.uniforms.pointShape.value=D,r.uniforms.colorMode.value=M,r.uniforms.customColor.value=s,r.uniforms.highlightColor.value=u,r.uniforms.minZ.value=C,r.uniforms.maxZ.value=z,r.uniforms.uHasUsableVertexColors.value=w?1:0,r.uniforms.uViewMode.value=N,r.uniforms.uHeightThreshold.value=T,r.uniforms.uAngleThreshold.value=A,r.uniforms.uEnableEDL.value=x?1:0,r.uniforms.uEdlStrength.value=b,r.clippingPlanes=h,r.needsUpdate=!0},[r,e,d,f,D,M,s,u,C,z,w,N,T,A,h,x,b]),g?o.jsx("points",{geometry:g,material:r,frustumCulled:!1}):null},Ze=X.lazy(()=>ae(()=>import("./MapboxTerrain-ChnLijVM.js"),__vite__mapDeps([0,1,2,3]))),Ge=({texture:t,color:e})=>{const{scene:d}=Z();return i.useEffect(()=>{const f=d.background;return t?d.background=t:d.background=new U(e),()=>{d.background=f,t?.dispose()}},[d,t,e]),null},qe=({controlsRef:t,onUpdate:e})=>{const{camera:d,size:f}=Z(),E=i.useRef(new V),P=i.useRef(new ee),m=i.useRef(0),v=i.useRef(-999),C=i.useRef(-1);return i.useEffect(()=>{if(!e)return;const z=()=>{const p=performance.now();if(p-m.current<60)return;const T=d.position.distanceToSquared(E.current)>1e-4,A=d.quaternion.angleTo(P.current)>.001;if(!T&&!A)return;const x=t.current?.target||new V(0,0,0),b=d.position.distanceTo(x),M=x.x-d.position.x,N=x.z-d.position.z,D=Math.round((Math.atan2(M,-N)*(180/Math.PI)+360)%360),s=b<25?0:b<60?1:b<120?2:b<300?3:b<600?4:b<1200?5:b<3e3?6:7,u=Math.abs(D-v.current)>=1,w=s!==C.current,g=d.position.distanceToSquared(E.current)>.25;if(!u&&!w&&!g&&p-m.current<150)return;m.current=p,E.current.copy(d.position),P.current.copy(d.quaternion),v.current=D,C.current=s;const r=d;e({dist:b,fov:r.fov||55,width:f.width,height:f.height,aspect:r.aspect||f.width/Math.max(f.height,1),near:r.near||.1,far:r.far||1e4,position:d.position.toArray(),quaternion:d.quaternion.toArray(),target:x.toArray()})},h=setInterval(z,100);return z(),()=>clearInterval(h)},[d,t,e,f]),null},Ye=({cave:t,trigger:e,offset:d})=>{const{camera:f,controls:E,size:P,invalidate:m}=Z();return i.useEffect(()=>{const v=t.bounds,C=d?.x||0,z=d?.z||0,h=-(d?.y||0),p=v.center.x+C,T=v.center.z+z,A=-v.center.y+h,{fitDist:x,frustumHalf:b,aspect:M}=Re(v,P,f.fov||55,"iso");f instanceof ye?(f.left=-b*M,f.right=b*M,f.top=b,f.bottom=-b,f.zoom=1,f.position.set(p+x*.707,T+x*.577,A+x*.707),f.updateProjectionMatrix()):(f.position.set(p+x*.707,T+x*.577,A+x*.707),f.near=Math.max(.1,x*.001),f.far=Math.max(x*25,1e4),f.updateProjectionMatrix()),E&&E.target&&(E.target.set(p,T,A),E.update()),m()},[t,e,f,E,d,P,m]),null},Xe=({isMeasuringMode:t,controlsRef:e,fitTrigger:d,cave:f,offset:E,onStationClick:P,anomalies:m,activeAnomalyId:v})=>{const{camera:C,scene:z,raycaster:h,gl:p}=Z(),T=i.useRef([]),A=i.useRef(null),x=i.useCallback(s=>{const u=p.domElement.getBoundingClientRect();if(u.width===0||u.height===0)return null;const w=s.clientX-u.left,g=s.clientY-u.top;return w<0||g<0||w>u.width||g>u.height?null:new be(w/u.width*2-1,-(g/u.height)*2+1)},[p]),b=i.useCallback((s,u)=>{e.current&&(T.current.push({pos:C.position.clone(),target:e.current.target.clone()}),T.current.length>20&&T.current.shift()),q.to(C.position,{x:s.x,y:s.y,z:s.z,duration:1.2,ease:"power2.inOut",onUpdate:()=>{e.current&&e.current.target.lerp(u,.1)},onComplete:()=>{e.current&&(e.current.target.copy(u),e.current.update())}})},[C,e]);i.useEffect(()=>{if(!v||!m||!e.current)return;const s=m.find(k=>k.id===v);if(!s)return;const u=E?.x||0,w=E?.z||0,g=-(E?.y||0),r=s.pos.x+u,S=s.pos.z+w,F=-s.pos.y+g,I=new V(r,S,F),H=new V(r+15,S+10,F+15);b(H,I)},[v,m,E,b,e]);const M=i.useCallback(()=>{const s=T.current.pop();s&&e.current&&(q.to(C.position,{x:s.pos.x,y:s.pos.y,z:s.pos.z,duration:1,ease:"power2.out"}),q.to(e.current.target,{x:s.target.x,y:s.target.y,z:s.target.z,duration:1,ease:"power2.out",onUpdate:()=>e.current.update()}))},[C,e]),N=i.useCallback(s=>{if(t)return;const u=x(s);if(!u)return;h.setFromCamera(u,C),h.params.Points={threshold:.5};const w=h.intersectObjects(z.children,!0);if(w.length>0){const g=w[0].point,r=C.position.clone(),S=new V().subVectors(g,r).normalize(),F=r.distanceTo(g),I=Math.max(0,F-3),H=r.clone().add(S.multiplyScalar(I));b(H,g)}},[C,z,h,t,b,x]),D=i.useCallback(s=>{if(A.current?.dragged||s.button!==0)return;const u=x(s);if(!u)return;h.setFromCamera(u,C),h.params.Points={threshold:.2};const g=h.intersectObjects(z.children,!0).find(r=>{let S=r.object;for(;S;){if(!S.visible)return!1;if(S.userData.measurementSource)return!0;S=S.parent}return!1});if(g){const r=g.object instanceof J&&g.index!==void 0?new V().fromBufferAttribute(g.object.geometry.getAttribute("position"),g.index).applyMatrix4(g.object.matrixWorld):g.point;P?.(-1,s.clientX,s.clientY,s.ctrlKey||s.metaKey,Ee(r,E))}},[C,z,h,t,P,x,E]);return i.useEffect(()=>{const s=r=>{A.current={x:r.clientX,y:r.clientY,dragged:!1}},u=r=>{const S=A.current;S&&Math.hypot(r.clientX-S.x,r.clientY-S.y)>5&&(S.dragged=!0)};p.domElement.addEventListener("pointerdown",s),p.domElement.addEventListener("pointermove",u),p.domElement.addEventListener("dblclick",N),p.domElement.addEventListener("click",D);const w=r=>{(r.metaKey||r.ctrlKey)&&r.key==="z"&&M()};window.addEventListener("keydown",w);const g=()=>M();return window.addEventListener("cave-navigation-undo",g),()=>{p.domElement.removeEventListener("pointerdown",s),p.domElement.removeEventListener("pointermove",u),p.domElement.removeEventListener("dblclick",N),p.domElement.removeEventListener("click",D),window.removeEventListener("keydown",w),window.removeEventListener("cave-navigation-undo",g)}},[N,D,M,p]),null},nt=({cave:t,options:e,onStationClick:d,onCameraUpdate:f,onStatusChange:E,fitTrigger:P,selectedStations:m,activeProfilePoints:v,isMeasuringMode:C,manualConnection:z,anomalies:h,activeAnomalyId:p,onSurfaceOffsetChange:T,lang:A="sk"})=>{const[x,b]=i.useState(!1),[M,N]=i.useState(!0),D=i.useRef(null),s=i.useRef(null),u=i.useRef(null),w=i.useMemo(()=>{const n=t.segments?.length||0,a=t.stations?.length||0,c=t.pointCount||0,L=t.scraps?.length||0;return n>800||a>800||c>1e5||L>80},[t]),g=i.useRef(null),r=i.useRef(null);i.useEffect(()=>{if(!r.current||!D.current)return;const n=D.current,a=r.current,c=L=>{n.enabled=!L.value};return a.addEventListener("dragging-changed",c),()=>{a.removeEventListener("dragging-changed",c)}},[e.terrainCalibrationMode]);const S=i.useCallback(()=>{if(g.current&&T){const n=g.current.position;T({x:Number(n.x.toFixed(2)),y:Number((-n.z).toFixed(2)),z:Number(n.y.toFixed(2))})}},[T]),F=()=>{b(!0),N(!1),u.current&&(clearTimeout(u.current),u.current=null)},I=()=>{b(!1),u.current&&clearTimeout(u.current),u.current=setTimeout(()=>{N(!0)},180)},H=i.useCallback(()=>{x||b(!0),M&&N(!1),s.current&&clearTimeout(s.current),s.current=setTimeout(()=>{b(!1),s.current=null},400),u.current&&clearTimeout(u.current),u.current=setTimeout(()=>{N(!0)},180)},[x,M]);i.useEffect(()=>()=>{s.current&&clearTimeout(s.current),u.current&&clearTimeout(u.current)},[]);const k=Math.sqrt(t.bounds.size.x**2+t.bounds.size.y**2+t.bounds.size.z**2),l=i.useMemo(()=>{const n=t.stationLabels.filter(a=>a.gps&&a.gps.lat&&a.gps.lon);if(n.length>0){const a=n.reduce((L,R)=>L+R.gps.lat,0)/n.length,c=n.reduce((L,R)=>L+R.gps.lon,0)/n.length;return{lat:a,lng:c}}return null},[t.stationLabels]),O=i.useMemo(()=>{if(!e.colorBackground2)return null;const n=document.createElement("canvas");n.width=2,n.height=512;const a=n.getContext("2d");if(!a)return null;const c=a.createLinearGradient(0,0,0,512);c.addColorStop(0,e.colorBackground),c.addColorStop(1,e.colorBackground2),a.fillStyle=c,a.fillRect(0,0,2,512);const L=new Ce(n);return L.colorSpace=ve,L},[e.colorBackground,e.colorBackground2]),j=i.useMemo(()=>{const n=[];if(e.showClipping&&n.push(new Y(new V(0,-1,0),e.clippingHeight-t.centerOffset.z)),e.showProfileClipping&&v&&v.length===2){const a=v[0],c=v[1],L=new V(a.origX-(a.centerX||0),0,-(a.origY-(a.centerY||0))),R=new V(c.origX-(a.centerX||0),0,-(c.origY-(a.centerY||0))),B=new V().subVectors(R,L).normalize();if(B.lengthSq()>1e-4){const W=new V(-B.z,0,B.x);e.profileClipFlip&&W.multiplyScalar(-1),n.push(new Y(W,-W.dot(L)-e.profileClipOffset))}}return n},[e.showClipping,e.clippingHeight,e.showProfileClipping,e.profileClipFlip,e.profileClipOffset,v,t.centerOffset.z]),y=i.useMemo(()=>e.excludeModelFromClipping?[]:j,[e.excludeModelFromClipping,j]);return o.jsxs(ue,{id:"nextgen-cave-canvas",style:{isolation:"isolate"},frameloop:e.autoRotate||x||!M?"always":"demand",dpr:w?1:typeof window<"u"&&window.devicePixelRatio>1.5?1.5:1,gl:{antialias:!w,alpha:!1,preserveDrawingBuffer:!0,powerPreference:"high-performance",localClippingEnabled:!0},camera:{fov:55,near:.1,far:Math.max(k*20,1e4)},children:[o.jsx(Ge,{texture:O,color:e.colorBackground}),o.jsx(Me,{projection:e.cameraProjection??"perspective",cave:t,controlsRef:D}),o.jsx(Xe,{isMeasuringMode:C,controlsRef:D,cave:t,offset:e.caveCalibrationOffset,onStationClick:d,anomalies:h,activeAnomalyId:p}),o.jsx("ambientLight",{intensity:.32}),o.jsx("hemisphereLight",{color:"#ffffff",groundColor:"#334155",intensity:.42}),o.jsx("directionalLight",{position:[4,6,3],intensity:1.15,color:"#ffffff",castShadow:!1}),o.jsx("directionalLight",{position:[-5,2,-4],intensity:.4,color:"#e2e8f0"}),o.jsx("directionalLight",{position:[-4,5,6],intensity:.6,color:"#bae6fd"}),o.jsx("directionalLight",{position:[0,-3,2],intensity:.18,color:"#fef3c7"}),(e.enableEDL??!0)&&o.jsx(je,{strength:e.edlStrength||1,radius:e.edlRadius||1}),o.jsxs("group",{position:[e.caveCalibrationOffset?.x||0,e.caveCalibrationOffset?.z||0,-(e.caveCalibrationOffset?.y||0)],children:[o.jsx(ze,{cave:t,options:e}),o.jsx(Pe,{cave:t,options:e,onStationClick:d}),o.jsx(Te,{cave:t,options:e,showSplay:e.showSplay,showAltitude:e.traverseAltitude,clippingPlanes:j,isMoving:!M,isLargeModel:w}),o.jsx("group",{visible:M,children:o.jsx(De,{cave:t,options:e,showNames:e.showStationNames,showAltitudes:e.showStationAlt})}),o.jsx("group",{visible:M||!w,children:o.jsx(ke,{cave:t,options:e})}),e.enableSplayWalls&&o.jsx(Le,{cave:t,options:e,showAltitude:e.splayAltitude??!0,clippingPlanes:y,isMoving:!M,onStatusChange:E}),(e.showScraps||e.accurateScraps||e.scrapsWireframe)&&t.scraps?.length>0&&o.jsx(Ae,{cave:t,options:e,opacity:e.scrapsOpacity,showSolid:e.showScraps&&(t.pointCount===0?e.scrapsSolid:e.smoothScraps||e.accurateScraps),showWire:e.scrapsWireframe,showAltitude:e.scrapsAltitude,smooth:e.smoothScraps&&!e.accurateScraps,showRender:e.showRenderCave,caveTexture:e.caveTexture,renderOpacity:e.renderOpacity,isMoving:x,isFullyIdle:M,clippingPlanes:j,onProcessingStart:E?n=>E({msg:n,type:"progress"}):void 0,onProcessingEnd:E?()=>E(null):void 0}),C&&o.jsx(Ne,{stations:m??[]}),!C&&m&&m.length===2&&o.jsx(He,{p1:m[0].pos,p2:m[1].pos}),m&&m.length===3&&o.jsx(Fe,{p1:m[0].pos,p2:m[1].pos,p3:m[2].pos}),m&&m.length===1&&m[0].distToSurf!==null&&m[0].surfaceZ!==null&&o.jsx(Ve,{station:m[0],lang:A}),e.placedCaver&&o.jsx(Ie,{pos:e.placedCaver.pos,pose:e.placedCaver.pose,clippingPlanes:j}),o.jsxs("group",{visible:e.showScraps,userData:{measurementSource:!0},children:[t.pointCloudUrl&&o.jsx(Oe,{url:t.pointCloudUrl,pointSize:e.pointCloudSize||1,brightness:e.pointCloudBrightness||1,plasticity:e.pointCloudPlasticity||1,pointShape:e.pointCloudShape||"diamond",colorMode:e.pointCloudColorMode||"original",customColor:e.pointCloudCustomColor||"#b3a694",highlightColor:e.colorClippingEdges||"#ff4444",minZ:t.bounds.min.z,maxZ:t.bounds.max.z,clippingPlanes:y,viewMode:e.pointCloudViewMode||"all",heightThreshold:e.pointCloudHeightThreshold??.4,angleThreshold:e.pointCloudAngleThreshold??.5,enableEDL:e.enableEDL!==!1,edlStrength:e.edlStrength||1}),!t.pointCloudUrl&&t.points&&t.pointCount>0&&o.jsx(Ue,{cave:t,pointSize:e.pointCloudSize||1,brightness:e.pointCloudBrightness||1,plasticity:e.pointCloudPlasticity||1,pointShape:e.pointCloudShape||"diamond",colorMode:e.pointCloudColorMode||"original",customColor:e.pointCloudCustomColor||"#b3a694",highlightColor:e.colorClippingEdges||"#ff4444",minZ:t.bounds.min.z,maxZ:t.bounds.max.z,clippingPlanes:y,viewMode:e.pointCloudViewMode||"all",heightThreshold:e.pointCloudHeightThreshold??.4,angleThreshold:e.pointCloudAngleThreshold??.5,enableEDL:e.enableEDL!==!1,edlStrength:e.edlStrength||1})]}),h&&h.map(n=>{const a=p===n.id,c=n.pos.x,L=n.pos.z,R=-n.pos.y;if(n.type==="chimney")return o.jsxs("group",{position:[c,L,R],children:[o.jsxs("mesh",{children:[o.jsx("coneGeometry",{args:[.4,n.size,8]}),o.jsx("meshBasicMaterial",{color:a?"#f43f5e":"#f97316",transparent:!0,opacity:a?.6:.3,wireframe:!a})]}),o.jsx("pointLight",{color:"#f97316",intensity:a?2:.5,distance:8})]},n.id);if(n.type==="window")return o.jsxs("group",{position:[c,L,R],children:[o.jsxs("mesh",{children:[o.jsx("sphereGeometry",{args:[.6,16,16]}),o.jsx("meshBasicMaterial",{color:a?"#06b6d4":"#0284c7",transparent:!0,opacity:a?.6:.3,wireframe:!a})]}),o.jsx("pointLight",{color:"#0284c7",intensity:a?2:.5,distance:6})]},n.id);if(n.type==="dome")return o.jsxs("group",{position:[c,L,R],children:[o.jsxs("mesh",{children:[o.jsx("sphereGeometry",{args:[.8,16,16]}),o.jsx("meshBasicMaterial",{color:a?"#a855f7":"#8b5cf6",transparent:!0,opacity:a?.6:.3,wireframe:!a})]}),o.jsx("pointLight",{color:"#8b5cf6",intensity:a?2:.5,distance:8})]},n.id);if(n.type==="fracture"){let B=[0,0,0];if(n.normal){const W=new V(n.normal.x,n.normal.z,-n.normal.y),ie=new V(0,1,0),se=new ee().setFromUnitVectors(ie,W),G=new xe().setFromQuaternion(se);B=[G.x,G.y,G.z]}return o.jsxs("group",{position:[c,L,R],children:[o.jsxs("mesh",{rotation:B,children:[o.jsx("boxGeometry",{args:[n.size,.05,n.size]}),o.jsx("meshBasicMaterial",{color:a?"#ec4899":"#d946ef",transparent:!0,opacity:a?.7:.4,wireframe:!a})]}),o.jsx("pointLight",{color:"#d946ef",intensity:a?2:.5,distance:8})]},n.id)}return null}),l&&e.showMapboxTerrain&&e.mapboxToken&&o.jsx("group",{ref:g,position:[e.surfaceOffset?.x||0,e.surfaceOffset?.z||0,-(e.surfaceOffset?.y||0)],children:o.jsx(X.Suspense,{fallback:null,children:o.jsx(Ze,{lat:l.lat,lng:l.lng,radius:e.mapboxRadius||2,zoom:e.mapboxZoom||13,opacity:e.mapboxOpacity||.5,visible:e.showMapboxTerrain,mapboxToken:e.mapboxToken})})}),e.terrainCalibrationMode&&e.showMapboxTerrain&&g.current&&o.jsx(ce,{ref:r,object:g.current,mode:"translate",onChange:S})]}),o.jsx(Ye,{cave:t,trigger:P,offset:e.caveCalibrationOffset}),o.jsx(pe,{alignment:"bottom-right",margin:[60,60],children:o.jsx(de,{axisColors:["#ef4444","#84cc16","#3b82f6"],labelColor:"white",labels:["V","H","J"]})}),o.jsx(fe,{ref:D,makeDefault:!0,enableDamping:!w,dampingFactor:w?.2:.05,onStart:F,onEnd:I,onChange:H}),o.jsx(qe,{controlsRef:D,onUpdate:f})]})};export{nt as default};
