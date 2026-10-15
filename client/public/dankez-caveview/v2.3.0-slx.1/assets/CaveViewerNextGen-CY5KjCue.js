import{r as s,j as a}from"./vendor-react-CpJAkXKM.js";import{u as I,b as X,C as re,T as ne,G as ie,a as se,O as ae}from"./vendor-r3f-AWFRcPcs.js";import{ak as le,M as ue,i as R,n as q,aa as V,Y as K,D as Q,al as ce,K as pe,d as k,a2 as de,a5 as fe,am as me,an as he,ao as ge,c as $,o as Z,ad as _,ap as ve,a7 as Ce,z as Y,Q as xe,E as ye}from"./vendor-three-BuOvmkGZ.js";import{g as W}from"./vendor-utils-xgxdCp6f.js";import{h as we,D as J,d as ee,e as be}from"./index-6leudrh7.js";import{T as Me}from"./vendor-geo-B2T2JNVG.js";import{P as Se,b as ze,a as Ee,c as Te,E as je,S as De,M as Pe,T as Ne,C as Ae}from"./ProjectionController-BS17__tS.js";const te=`
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
`,oe=`
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
uniform int pointShape; // 0 square, 1 sphere, 2 rounded diamond, 3 hex

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

    vec2 hexCoord = abs(coord);
    float hexDist = max(hexCoord.x * 0.8660254 + hexCoord.y * 0.5, hexCoord.y);
    return 1.0 - smoothstep(0.88, 0.98, hexDist);
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

    // 6. Highlight for Clipping Edges
    if (hasClip && minClipDist < 0.15) {
        float highlightStrength = 1.0 - (minClipDist / 0.15);
        finalColor = mix(finalColor, highlightColor, highlightStrength * 0.9);
    }

    gl_FragColor = vec4(finalColor, 1.0);
}
`,He=({url:t,pointSize:e=1,brightness:c=1,plasticity:m=1,pointShape:v=J,colorMode:l="original",customColor:u="#ffffff",highlightColor:n="#ff4444",minZ:i=-100,maxZ:y=100,clippingPlanes:f=[],viewMode:h="all",heightThreshold:b=.4,angleThreshold:T=.5})=>{const[A,S]=s.useState(new Map),{camera:w}=I(),P=s.useRef(null),p=s.useRef(new Map),x=s.useRef(new le),z=s.useRef(new ue),g=l==="elevation"?1:l==="natural"?2:0,E=h==="floor"?1:h==="ceiling"?2:h==="contour"?3:h==="heatmap"?4:0,L=ee(v),j=s.useMemo(()=>new R(u),[u]),N=s.useMemo(()=>new R(n),[n]);return s.useEffect(()=>{let r=!0;const o=new Worker(new URL("/dankez-caveview/v2.3.0-slx.1/assets/pointcloud.worker-CM9KUmIQ.js",import.meta.url),{type:"module"});return P.current=o,o.onmessage=C=>{const d=C.data;if(d.type==="POINTCLOUD_CHUNK"&&d.points&&d.colors&&d.normals&&d.intensity&&d.relHeight&&d.bounds&&d.id){const D=new q;D.setAttribute("position",new V(d.points,3)),D.setAttribute("color",new V(d.colors,3)),D.setAttribute("normal",new V(d.normals,3)),D.setAttribute("intensity",new V(d.intensity,1)),D.setAttribute("relHeight",new V(d.relHeight,1)),D.computeBoundingSphere();const U=new K({uniforms:{pointSize:{value:e},brightness:{value:c},plasticity:{value:m},pointShape:{value:L},colorMode:{value:g},customColor:{value:j},highlightColor:{value:N},minZ:{value:i},maxZ:{value:y},uHasUsableVertexColors:{value:we(d.colors,d.vertexCount||0)?1:0},uViewMode:{value:E},uHeightThreshold:{value:b},uAngleThreshold:{value:T}},vertexShader:te,fragmentShader:oe,transparent:!1,depthWrite:!0,depthTest:!0,clipping:!0,clippingPlanes:f,side:Q}),H=new ce(D,U);H.frustumCulled=!1;const F=new pe(new k(d.bounds.min.x,d.bounds.min.y,d.bounds.min.z),new k(d.bounds.max.x,d.bounds.max.y,d.bounds.max.z)),M={points:H,bounds:F};p.current.set(d.id,M),S(O=>{const G=new Map(O);return G.set(d.id,M),G})}},fetch(t).then(C=>C.arrayBuffer()).then(C=>{r&&o.postMessage({buffer:C},[C])}).catch(C=>console.error("Failed to fetch pointcloud:",C)),()=>{r=!1,o.terminate(),p.current.forEach(C=>{C.points.geometry.dispose(),Array.isArray(C.points.material)?C.points.material.forEach(d=>d.dispose()):C.points.material.dispose()}),p.current.clear(),S(new Map)}},[t]),s.useEffect(()=>{A.forEach(r=>{const o=r.points.material;o.uniforms&&(o.uniforms.pointSize&&(o.uniforms.pointSize.value=e),o.uniforms.brightness&&(o.uniforms.brightness.value=c),o.uniforms.plasticity&&(o.uniforms.plasticity.value=m),o.uniforms.pointShape&&(o.uniforms.pointShape.value=L),o.uniforms.colorMode&&(o.uniforms.colorMode.value=g),o.uniforms.customColor&&(o.uniforms.customColor.value=j),o.uniforms.highlightColor&&(o.uniforms.highlightColor.value=N),o.uniforms.minZ&&(o.uniforms.minZ.value=i),o.uniforms.maxZ&&(o.uniforms.maxZ.value=y),o.uniforms.uViewMode&&(o.uniforms.uViewMode.value=E),o.uniforms.uHeightThreshold&&(o.uniforms.uHeightThreshold.value=b),o.uniforms.uAngleThreshold&&(o.uniforms.uAngleThreshold.value=T)),o.clippingPlanes=f,o.needsUpdate=!0})},[e,c,m,L,l,u,n,j,N,i,y,f,A,h,b,T]),X(()=>{const r=x.current,o=z.current;o.multiplyMatrices(w.projectionMatrix,w.matrixWorldInverse),r.setFromProjectionMatrix(o),A.forEach(C=>{C.points.visible=r.intersectsBox(C.bounds)})}),a.jsx("group",{children:Array.from(A.values()).map((r,o)=>a.jsx("primitive",{object:r.points},o))})};function Le(t){const e=t.points,c=t.pointCount,m=new Float32Array(c);if(!e||c===0)return m;const l=1/Math.max(.2,Math.max(t.bounds.size.x,t.bounds.size.y,1)/280),u=new Map;for(let n=0;n<c;n++){const i=n*3,y=`${Math.floor(e[i]*l)},${Math.floor(e[i+1]*l)}`,f=e[i+2],h=u.get(y);h?(f<h.min&&(h.min=f),f>h.max&&(h.max=f)):u.set(y,{min:f,max:f})}for(let n=0;n<c;n++){const i=n*3,y=`${Math.floor(e[i]*l)},${Math.floor(e[i+1]*l)}`,f=u.get(y);if(!f){m[n]=0;continue}const h=(f.max-f.min)/2;m[n]=h>.02?(e[i+2]-(f.min+f.max)/2)/h:-1}return m}function ke(t){const e=t.pointCount;return!t.pointColors||t.pointColors.length<e*3?!1:be(t,e)}function Ue(t){const e=t.pointCount;return!(!t.pointNormals||t.pointNormals.length<e*3||t.hasPointNormals===!1)}const Ve=({cave:t,pointSize:e=1,brightness:c=1,plasticity:m=1,pointShape:v=J,colorMode:l="original",customColor:u="#ffffff",highlightColor:n="#ff4444",minZ:i=-100,maxZ:y=100,clippingPlanes:f=[],viewMode:h="all",heightThreshold:b=.4,angleThreshold:T=.5})=>{const A=l==="elevation"?1:l==="natural"?2:0,S=h==="floor"?1:h==="ceiling"?2:h==="contour"?3:h==="heatmap"?4:0,w=ee(v),P=s.useMemo(()=>new R(u),[u]),p=s.useMemo(()=>new R(n),[n]),x=s.useMemo(()=>ke(t),[t]),z=s.useMemo(()=>{const E=t.pointCount,L=t.points;if(!L||E===0)return null;const j=new Float32Array(E*3),N=new Float32Array(E*3),r=new Float32Array(E*3),o=new Float32Array(E),C=Le(t),d=x&&!!t.pointColors&&t.pointColors.length>=E*3,D=Ue(t),U=!!t.pointIntensity&&t.pointIntensity.length>=E;for(let F=0;F<E;F++){const M=F*3;j[M]=L[M],j[M+1]=L[M+2],j[M+2]=-L[M+1],d?(N[M]=t.pointColors[M],N[M+1]=t.pointColors[M+1],N[M+2]=t.pointColors[M+2]):(N[M]=0,N[M+1]=0,N[M+2]=0),D&&(r[M]=t.pointNormals[M],r[M+1]=t.pointNormals[M+2],r[M+2]=-t.pointNormals[M+1]),o[F]=U?t.pointIntensity[F]:1}const H=new q;return H.setAttribute("position",new V(j,3)),H.setAttribute("color",new V(N,3)),H.setAttribute("normal",new V(r,3)),H.setAttribute("intensity",new V(o,1)),H.setAttribute("relHeight",new V(C,1)),H.computeBoundingSphere(),H},[t,x]),g=s.useMemo(()=>new K({uniforms:{pointSize:{value:e},brightness:{value:c},plasticity:{value:m},pointShape:{value:w},colorMode:{value:A},customColor:{value:P},highlightColor:{value:p},minZ:{value:i},maxZ:{value:y},uHasUsableVertexColors:{value:x?1:0},uViewMode:{value:S},uHeightThreshold:{value:b},uAngleThreshold:{value:T}},vertexShader:te,fragmentShader:oe,transparent:!1,depthWrite:!0,depthTest:!0,clipping:!0,clippingPlanes:f,side:Q}),[]);return s.useEffect(()=>{if(z)return()=>{z.dispose()}},[z]),s.useEffect(()=>()=>{g.dispose()},[g]),s.useEffect(()=>{g.uniforms.pointSize.value=e,g.uniforms.brightness.value=c,g.uniforms.plasticity.value=m,g.uniforms.pointShape.value=w,g.uniforms.colorMode.value=A,g.uniforms.customColor.value=P,g.uniforms.highlightColor.value=p,g.uniforms.minZ.value=i,g.uniforms.maxZ.value=y,g.uniforms.uHasUsableVertexColors.value=x?1:0,g.uniforms.uViewMode.value=S,g.uniforms.uHeightThreshold.value=b,g.uniforms.uAngleThreshold.value=T,g.clippingPlanes=f,g.needsUpdate=!0},[g,e,c,m,w,A,P,p,i,y,x,S,b,T,f]),z?a.jsx("points",{geometry:z,material:g,frustumCulled:!1}):null},Fe={uniforms:{tDiffuse:{value:null},tDepth:{value:null},size:{value:new $},edlStrength:{value:1},radius:{value:1}},vertexShader:`
    varying vec2 vUv;
    void main() {
      vUv = uv;
      gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
    }
  `,fragmentShader:`
    uniform sampler2D tDiffuse;
    uniform sampler2D tDepth;
    uniform vec2 size;
    uniform float edlStrength;
    uniform float radius;
    varying vec2 vUv;

    float getDepth(vec2 uv) {
      return texture2D(tDepth, uv).r;
    }

    void main() {
      vec4 color = texture2D(tDiffuse, vUv);
      float d = getDepth(vUv);
      
      if (d == 1.0) {
        gl_FragColor = color;
        return;
      }

      float res = 0.0;
      vec2 pixelSize = 1.0 / size;

      // Sample 8 neighbors
      res += max(0.0, d - getDepth(vUv + vec2(1, 0) * pixelSize * radius));
      res += max(0.0, d - getDepth(vUv + vec2(-1, 0) * pixelSize * radius));
      res += max(0.0, d - getDepth(vUv + vec2(0, 1) * pixelSize * radius));
      res += max(0.0, d - getDepth(vUv + vec2(0, -1) * pixelSize * radius));
      res += max(0.0, d - getDepth(vUv + vec2(1, 1) * pixelSize * radius));
      res += max(0.0, d - getDepth(vUv + vec2(-1, -1) * pixelSize * radius));
      res += max(0.0, d - getDepth(vUv + vec2(1, -1) * pixelSize * radius));
      res += max(0.0, d - getDepth(vUv + vec2(-1, 1) * pixelSize * radius));

      float shading = clamp(1.0 - res * edlStrength * 100.0, 0.0, 1.0);
      gl_FragColor = vec4(color.rgb * shading, color.a);
    }
  `},Ie=({strength:t=1,radius:e=1})=>{const{gl:c,scene:m,camera:v,size:l}=I(),u=s.useMemo(()=>{const n=new de(l.width,l.height);n.depthTexture=new fe(l.width,l.height);const i=new me(c,n);i.addPass(new he(m,v));const y=new ge(Fe);return y.uniforms.tDepth.value=n.depthTexture,y.uniforms.size.value.set(l.width,l.height),y.uniforms.edlStrength.value=t,y.uniforms.radius.value=e,i.addPass(y),i},[c,m,v]);return s.useEffect(()=>{u.setSize(l.width,l.height);const n=u.passes[1];n&&n.uniforms&&n.uniforms.size&&n.uniforms.size.value.set(l.width,l.height)},[u,l.width,l.height]),s.useEffect(()=>{const n=u.passes[1];n&&n.uniforms&&(n.uniforms.edlStrength.value=t,n.uniforms.radius.value=e)},[u,t,e]),s.useEffect(()=>()=>{u.passes.forEach(y=>{y.dispose&&y.dispose()});const n=u.readBuffer;n&&(n.depthTexture&&n.depthTexture.dispose(),n.dispose());const i=u.writeBuffer;i&&(i.depthTexture&&i.depthTexture.dispose(),i.dispose())},[u]),X(()=>{u.render()},1),null};function Oe(t){(Array.isArray(t)?t:[t]).forEach(c=>{Object.values(c).forEach(m=>{m instanceof ve&&m.dispose()}),c.dispose()})}function B(t){t&&t.traverse(e=>{e instanceof Z&&(e.geometry?.dispose(),e.material&&Oe(e.material))})}const Re=({lat:t,lng:e,radius:c,zoom:m=13,opacity:v=1,visible:l=!0,mapboxToken:u,position:n=[0,0,0]})=>{const[i,y]=s.useState(null),f=s.useRef(null),h=s.useMemo(()=>new Me({token:u}),[u]);return s.useEffect(()=>{if(!l)return;let b=!0;const T=[t,e];return(async()=>{try{const S=await h.getTerrainRgb(T,c,m);b?(S.traverse(w=>{if(w instanceof Z&&(w.material.transparent=v<1,w.material.opacity=v,!(w.material instanceof _))){const P=w.material;w.material=new _({map:P.map,transparent:v<1,opacity:v}),P.dispose?.()}}),y(w=>(w!==S&&B(w),f.current=S,S))):B(S)}catch(S){console.error("Failed to load Mapbox terrain:",S)}})(),()=>{b=!1}},[t,e,c,m,h,l]),s.useEffect(()=>()=>{B(f.current),f.current=null},[]),s.useEffect(()=>{i&&(i.visible=l,i.traverse(b=>{b instanceof Z&&(b.material.transparent=v<1,b.material.opacity=v)}))},[l,v,i]),!i||!l?null:a.jsx("primitive",{object:i,position:n})},We=({texture:t,color:e})=>{const{scene:c}=I();return s.useEffect(()=>{const m=c.background;return t?c.background=t:c.background=new R(e),()=>{c.background=m,t?.dispose()}},[c,t,e]),null},Be=({controlsRef:t,onUpdate:e})=>{const{camera:c,size:m}=I();return s.useEffect(()=>{if(!e)return;const v=()=>{const u=t.current?.target||new k(0,0,0),n=c.position.distanceTo(u),i=c;c.updateMatrixWorld(!0),e({dist:n,fov:i.fov||55,width:m.width,height:m.height,aspect:i.aspect||m.width/Math.max(m.height,1),near:i.near||.1,far:i.far||1e4,position:c.position.toArray(),quaternion:c.quaternion.toArray(),target:u.toArray()})},l=setInterval(v,200);return v(),()=>clearInterval(l)},[c,t,e,m]),null},Ze=({cave:t,trigger:e,offset:c})=>{const{camera:m,controls:v}=I();return s.useEffect(()=>{const l=t.bounds,u=Math.sqrt(l.size.x**2+l.size.y**2+l.size.z**2),n=Math.max(u*2,50),i=c?.x||0,y=c?.z||0,f=-(c?.y||0),h=l.center.x+i,b=l.center.z+y,T=-l.center.y+f;m.position.set(h+n,b+n*.8,T+n),m.near=.1,m.far=Math.max(u*25,1e4),m.updateProjectionMatrix(),v&&v.target&&(v.target.set(h,b,T),v.update())},[t,e,m,v,c]),null},Ge=({isMeasuringMode:t,controlsRef:e,fitTrigger:c,cave:m,offset:v,onStationClick:l,anomalies:u,activeAnomalyId:n})=>{const{camera:i,scene:y,raycaster:f,gl:h}=I(),b=s.useRef([]),T=s.useCallback(p=>{const x=h.domElement.getBoundingClientRect();if(x.width===0||x.height===0)return null;const z=p.clientX-x.left,g=p.clientY-x.top;return z<0||g<0||z>x.width||g>x.height?null:new $(z/x.width*2-1,-(g/x.height)*2+1)},[h]),A=s.useCallback((p,x)=>{e.current&&(b.current.push({pos:i.position.clone(),target:e.current.target.clone()}),b.current.length>20&&b.current.shift()),W.to(i.position,{x:p.x,y:p.y,z:p.z,duration:1.2,ease:"power2.inOut",onUpdate:()=>{e.current&&e.current.target.lerp(x,.1)},onComplete:()=>{e.current&&(e.current.target.copy(x),e.current.update())}})},[i,e]);s.useEffect(()=>{if(!n||!u||!e.current)return;const p=u.find(o=>o.id===n);if(!p)return;const x=v?.x||0,z=v?.z||0,g=-(v?.y||0),E=p.pos.x+x,L=p.pos.z+z,j=-p.pos.y+g,N=new k(E,L,j),r=new k(E+15,L+10,j+15);A(r,N)},[n,u,v,A,e]);const S=s.useCallback(()=>{const p=b.current.pop();p&&e.current&&(W.to(i.position,{x:p.pos.x,y:p.pos.y,z:p.pos.z,duration:1,ease:"power2.out"}),W.to(e.current.target,{x:p.target.x,y:p.target.y,z:p.target.z,duration:1,ease:"power2.out",onUpdate:()=>e.current.update()}))},[i,e]),w=s.useCallback(p=>{if(t)return;const x=T(p);if(!x)return;f.setFromCamera(x,i),f.params.Points={threshold:.5};const z=f.intersectObjects(y.children,!0);if(z.length>0){const g=z[0].point,E=i.position.clone(),L=new k().subVectors(g,E).normalize(),j=E.distanceTo(g),N=Math.max(0,j-3),r=E.clone().add(L.multiplyScalar(N));A(r,g)}},[i,y,f,t,A,T]),P=s.useCallback(p=>{if(!t)return;const x=T(p);if(!x)return;f.setFromCamera(x,i),f.params.Points={threshold:.2};const z=f.intersectObjects(y.children,!0);if(z.length>0){const g=z[0].point;l?.(-1,p.clientX,p.clientY,p.ctrlKey,g)}},[i,y,f,t,l,T]);return s.useEffect(()=>{h.domElement.addEventListener("dblclick",w),h.domElement.addEventListener("click",P);const p=z=>{(z.metaKey||z.ctrlKey)&&z.key==="z"&&S()};window.addEventListener("keydown",p);const x=()=>S();return window.addEventListener("cave-navigation-undo",x),()=>{h.domElement.removeEventListener("dblclick",w),h.domElement.removeEventListener("click",P),window.removeEventListener("keydown",p),window.removeEventListener("cave-navigation-undo",x)}},[w,P,S,h]),null},Je=({cave:t,options:e,onStationClick:c,onCameraUpdate:m,onStatusChange:v,fitTrigger:l,selectedStations:u,activeProfilePoints:n,isMeasuringMode:i,manualConnection:y,anomalies:f,activeAnomalyId:h,onSurfaceOffsetChange:b})=>{const[T,A]=s.useState(!1),S=s.useRef(null),w=s.useRef(null),P=s.useRef(null),p=s.useRef(null);s.useEffect(()=>{if(!p.current||!S.current)return;const r=S.current,o=p.current,C=d=>{r.enabled=!d.value};return o.addEventListener("dragging-changed",C),()=>{o.removeEventListener("dragging-changed",C)}},[e.terrainCalibrationMode]);const x=s.useCallback(()=>{if(P.current&&b){const r=P.current.position;b({x:Number(r.x.toFixed(2)),y:Number((-r.z).toFixed(2)),z:Number(r.y.toFixed(2))})}},[b]),z=s.useCallback(()=>{T||A(!0),w.current&&clearTimeout(w.current),w.current=setTimeout(()=>{A(!1),w.current=null},800)},[T]);s.useEffect(()=>()=>{w.current&&clearTimeout(w.current)},[]);const g=Math.sqrt(t.bounds.size.x**2+t.bounds.size.y**2+t.bounds.size.z**2),E=s.useMemo(()=>{const r=t.stationLabels.filter(o=>o.gps&&o.gps.lat&&o.gps.lon);if(r.length>0){const o=r.reduce((d,D)=>d+D.gps.lat,0)/r.length,C=r.reduce((d,D)=>d+D.gps.lon,0)/r.length;return{lat:o,lng:C}}return null},[t.stationLabels]),L=s.useMemo(()=>{if(!e.colorBackground2)return null;const r=document.createElement("canvas");r.width=2,r.height=512;const o=r.getContext("2d");if(!o)return null;const C=o.createLinearGradient(0,0,0,512);return C.addColorStop(0,e.colorBackground),C.addColorStop(1,e.colorBackground2),o.fillStyle=C,o.fillRect(0,0,2,512),new Ce(r)},[e.colorBackground,e.colorBackground2]),j=s.useMemo(()=>{const r=[];if(e.showClipping&&r.push(new Y(new k(0,-1,0),e.clippingHeight-t.centerOffset.z)),e.showProfileClipping&&n&&n.length===2){const o=n[0],C=n[1],d=new k(o.origX-(o.centerX||0),0,-(o.origY-(o.centerY||0))),D=new k(C.origX-(o.centerX||0),0,-(C.origY-(o.centerY||0))),U=new k().subVectors(D,d).normalize();if(U.lengthSq()>1e-4){const H=new k(-U.z,0,U.x);e.profileClipFlip&&H.multiplyScalar(-1),r.push(new Y(H,-H.dot(d)-e.profileClipOffset))}}return r},[e.showClipping,e.clippingHeight,e.showProfileClipping,e.profileClipFlip,e.profileClipOffset,n,t.centerOffset.z]),N=s.useMemo(()=>e.excludeModelFromClipping?[]:j,[e.excludeModelFromClipping,j]);return a.jsxs(re,{id:"nextgen-cave-canvas",gl:{antialias:!0,alpha:!1,preserveDrawingBuffer:!0,powerPreference:"high-performance",localClippingEnabled:!0},camera:{fov:55,near:.1,far:Math.max(g*20,1e4)},children:[a.jsx(We,{texture:L,color:e.colorBackground}),a.jsx(Se,{projection:e.cameraProjection??"perspective",cave:t,controlsRef:S}),a.jsx(Ge,{isMeasuringMode:i,controlsRef:S,cave:t,offset:e.caveCalibrationOffset,onStationClick:c,anomalies:f,activeAnomalyId:h}),a.jsx("ambientLight",{intensity:.16}),a.jsx("hemisphereLight",{color:"#dbeafe",groundColor:"#1e293b",intensity:.34}),a.jsx("directionalLight",{position:[4,6,3],intensity:.9,color:"#ffffff"}),a.jsx("directionalLight",{position:[-5,2,-4],intensity:.24,color:"#bfdbfe"}),a.jsx("directionalLight",{position:[-4,5,6],intensity:.46,color:"#7dd3fc"}),a.jsx("directionalLight",{position:[0,-3,2],intensity:.08,color:"#fef3c7"}),a.jsx(Ie,{strength:e.edlStrength||1,radius:e.edlRadius||1}),a.jsxs("group",{position:[e.caveCalibrationOffset?.x||0,e.caveCalibrationOffset?.z||0,-(e.caveCalibrationOffset?.y||0)],children:[a.jsx(ze,{cave:t,options:e}),a.jsx(Ee,{cave:t,options:e,showSplay:e.showSplay,showAltitude:e.traverseAltitude,clippingPlanes:j}),a.jsx(Te,{cave:t,options:e,showNames:e.showStationNames,showAltitudes:e.showStationAlt}),a.jsx(je,{cave:t,options:e}),e.showScraps&&t.scraps?.length>0&&a.jsx(De,{cave:t,options:e,opacity:e.scrapsOpacity,showSolid:t.pointCount===0?e.scrapsSolid:e.smoothScraps||e.accurateScraps,showWire:e.scrapsWireframe,showAltitude:e.scrapsAltitude,smooth:e.smoothScraps,showRender:e.showRenderCave,caveTexture:e.caveTexture,renderOpacity:e.renderOpacity,isMoving:T,clippingPlanes:j,onProcessingStart:v?r=>v({msg:r,type:"progress"}):void 0,onProcessingEnd:v?()=>v(null):void 0}),u&&u.length===2&&a.jsx(Pe,{p1:u[0].pos,p2:u[1].pos}),u&&u.length===3&&a.jsx(Ne,{p1:u[0].pos,p2:u[1].pos,p3:u[2].pos}),e.placedCaver&&a.jsx(Ae,{pos:e.placedCaver.pos,pose:e.placedCaver.pose,clippingPlanes:j}),t.pointCloudUrl&&a.jsx(He,{url:t.pointCloudUrl,pointSize:e.pointCloudSize||1,brightness:e.pointCloudBrightness||1,plasticity:e.pointCloudPlasticity||1,pointShape:e.pointCloudShape||"diamond",colorMode:e.pointCloudColorMode||"original",customColor:e.pointCloudCustomColor||"#b3a694",highlightColor:e.colorClippingEdges||"#ff4444",minZ:t.bounds.min.z,maxZ:t.bounds.max.z,clippingPlanes:N,viewMode:e.pointCloudViewMode||"all",heightThreshold:e.pointCloudHeightThreshold??.4,angleThreshold:e.pointCloudAngleThreshold??.5}),!t.pointCloudUrl&&t.points&&t.pointCount>0&&a.jsx(Ve,{cave:t,pointSize:e.pointCloudSize||1,brightness:e.pointCloudBrightness||1,plasticity:e.pointCloudPlasticity||1,pointShape:e.pointCloudShape||"diamond",colorMode:e.pointCloudColorMode||"original",customColor:e.pointCloudCustomColor||"#b3a694",highlightColor:e.colorClippingEdges||"#ff4444",minZ:t.bounds.min.z,maxZ:t.bounds.max.z,clippingPlanes:N,viewMode:e.pointCloudViewMode||"all",heightThreshold:e.pointCloudHeightThreshold??.4,angleThreshold:e.pointCloudAngleThreshold??.5}),f&&f.map(r=>{const o=h===r.id,C=r.pos.x,d=r.pos.z,D=-r.pos.y;if(r.type==="chimney")return a.jsxs("group",{position:[C,d,D],children:[a.jsxs("mesh",{children:[a.jsx("coneGeometry",{args:[.4,r.size,8]}),a.jsx("meshBasicMaterial",{color:o?"#f43f5e":"#f97316",transparent:!0,opacity:o?.6:.3,wireframe:!o})]}),a.jsx("pointLight",{color:"#f97316",intensity:o?2:.5,distance:8})]},r.id);if(r.type==="window")return a.jsxs("group",{position:[C,d,D],children:[a.jsxs("mesh",{children:[a.jsx("sphereGeometry",{args:[.6,16,16]}),a.jsx("meshBasicMaterial",{color:o?"#06b6d4":"#0284c7",transparent:!0,opacity:o?.6:.3,wireframe:!o})]}),a.jsx("pointLight",{color:"#0284c7",intensity:o?2:.5,distance:6})]},r.id);if(r.type==="fracture"){let U=[0,0,0];if(r.normal){const H=new k(r.normal.x,r.normal.z,-r.normal.y).normalize(),F=new k(0,1,0),M=new xe().setFromUnitVectors(F,H),O=new ye().setFromQuaternion(M);U=[O.x,O.y,O.z]}return a.jsxs("group",{position:[C,d,D],rotation:U,children:[a.jsxs("mesh",{children:[a.jsx("cylinderGeometry",{args:[r.size/4,r.size/4,.1,16]}),a.jsx("meshBasicMaterial",{color:o?"#d946ef":"#8b5cf6",transparent:!0,opacity:o?.5:.25,wireframe:!o})]}),a.jsx("pointLight",{color:"#8b5cf6",intensity:o?2:.5,distance:8})]},r.id)}return null}),E&&e.showMapboxTerrain&&e.mapboxToken&&a.jsx("group",{ref:P,position:[e.surfaceOffset?.x||0,e.surfaceOffset?.z||0,-(e.surfaceOffset?.y||0)],children:a.jsx(Re,{lat:E.lat,lng:E.lng,radius:e.mapboxRadius||2,zoom:e.mapboxZoom||13,opacity:e.mapboxOpacity||.5,visible:e.showMapboxTerrain,mapboxToken:e.mapboxToken})}),e.terrainCalibrationMode&&e.showMapboxTerrain&&P.current&&a.jsx(ne,{ref:p,object:P.current,mode:"translate",onChange:x})]}),a.jsx(Ze,{cave:t,trigger:l,offset:e.caveCalibrationOffset}),a.jsx(ie,{alignment:"bottom-right",margin:[60,60],children:a.jsx(se,{axisColors:["#ef4444","#84cc16","#3b82f6"],labelColor:"white",labels:["V","H","J"]})}),a.jsx(ae,{ref:S,makeDefault:!0,enableDamping:!0,dampingFactor:.05,onChange:z}),a.jsx(Be,{controlsRef:S,onUpdate:m})]})};export{Je as default};
