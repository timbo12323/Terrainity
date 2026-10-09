using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    internal enum GrassStudy { Blade, Clump, RepeatedPatch }

    internal static class GrassGenerator
    {
        internal static Mesh Build(GrassRecipe source, GrassStudy study)
        {
            var r = source.Copy(); r.Validate();
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var tint = new List<Vector2>(); var triangles = new List<int>();
            var wind = new List<Vector4>(); var bladeRoots = new List<Vector4>();
            // Random channels belong to the blade, independently of mesh subdivision and blade count.
            int sideSegments = r.fold > 0 ? Mathf.Max(2,r.widthSegments + r.widthSegments % 2) : r.widthSegments;
            Vector3 Center(float t, float height)
            {
                float u = Mathf.Clamp01((t-r.bendStart)/(1-r.bendStart));
                return new Vector3(0, height*t, height*(Mathf.Tan(r.lean*Mathf.Deg2Rad)*t + r.bend*u*u));
            }
            void Triangle(int a,int b,int c)
            {
                if (Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude > 1e-20f)
                { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            }
            void Quad(int a,int b,int c,int d,bool reverse = false)
            {
                if (reverse) { Triangle(a,c,b); Triangle(a,d,c); }
                else { Triangle(a,b,c); Triangle(a,c,d); }
            }
            void Blade(Vector3 root, float yaw, float height, float width, float phase, float response)
            {
                int stride = sideSegments+1, rows = r.lengthSegments+1, start = vertices.Count;
                int faces = r.thickness > 0 ? 2 : 1;
                var rotation = Quaternion.Euler(0,yaw,0);
                for (int face=0; face<faces; face++)
                    for (int row=0; row<rows; row++)
                    {
                        float t = row/(float)r.lengthSegments;
                        var center = Center(t,height);
                        var tangent = (Center(Mathf.Min(1,t+.001f),height)-Center(Mathf.Max(0,t-.001f),height)).normalized;
                        var frame = Quaternion.LookRotation(Vector3.Cross(Vector3.right,tangent).normalized,tangent)
                            * Quaternion.Euler(0,r.twist*t,0);
                        float bellyT = t <= r.bellyPosition ? t/r.bellyPosition : (1-t)/(1-r.bellyPosition);
                        float belly = Mathf.Sin(bellyT*Mathf.PI*.5f);
                        float profile = Mathf.Lerp(r.rootWidth,r.tipWidth,Mathf.Pow(t,r.taper)) + r.belly*belly*belly;
                        for (int col=0; col<stride; col++)
                        {
                            float x = col/(float)sideSegments*2-1;
                            float halfWidth = width*profile*.5f;
                            float depth = -Mathf.Abs(x)*halfWidth*Mathf.Sin(r.fold*Mathf.Deg2Rad);
                            if (faces == 2) depth += (face == 0 ? 1 : -1)*r.thickness*.5f*profile;
                            var cross = new Vector3(x*halfWidth*Mathf.Cos(r.fold*Mathf.Deg2Rad),0,depth);
                            var point = root+rotation*(center+frame*cross);
                            if (row == 0) point.y = root.y;
                            vertices.Add(point);
                            uv.Add(new Vector2(col/(float)sideSegments,t)); tint.Add(new Vector2(t,.75f*(1-t)*(1-t)));
                            wind.Add(new Vector4(height*t*t,height*t*t*t,phase,t));
                            bladeRoots.Add(new Vector4(root.x,root.z,height,response));
                        }
                    }
                for (int face=0; face<faces; face++)
                    for (int row=0; row<rows-1; row++)
                        for (int col=0; col<sideSegments; col++)
                        {
                            int a = start+face*rows*stride+row*stride+col;
                            Quad(a,a+1,a+stride+1,a+stride,face == 1);
                        }
                if (faces == 2)
                {
                    int back = rows*stride;
                    for (int row=0; row<rows-1; row++)
                    {
                        int a = start+row*stride;
                        Quad(a,a+stride,a+stride+back,a+back);
                        a += sideSegments;
                        Quad(a,a+back,a+stride+back,a+stride);
                    }
                    for (int col=0; col<sideSegments; col++)
                    {
                        int a = start+col; Quad(a,a+back,a+back+1,a+1);
                        a += (rows-1)*stride; Quad(a,a+1,a+back+1,a+back);
                    }
                }
            }
            void Clump(Vector3 root, int clumpIndex)
            {
                var placement = new System.Random(unchecked(r.seed+clumpIndex*104729));
                float clumpYaw = (float)placement.NextDouble()*360;
                for (int blade=0; blade<r.bladeCount; blade++)
                {
                    var random = new System.Random(unchecked(r.seed+blade*7919+clumpIndex*104729));
                    float Next() => (float)random.NextDouble();
                    float sample = Mathf.Lerp((blade+.5f)/r.bladeCount,Next(),r.irregularity);
                    float distance = r.radius*Mathf.Sqrt(Mathf.Lerp(r.gap*r.gap,1,sample));
                    float angle = (blade*137.50776f+clumpYaw+(Next()-.5f)*120*r.irregularity)*Mathf.Deg2Rad;
                    var offset = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*distance;
                    float yaw = clumpYaw+blade*137.50776f+(Next()-.5f)*360*r.directionVariation;
                    float height = r.height*(1+(Next()*2-1)*r.heightVariation);
                    float width = r.width*(1+(Next()*2-1)*r.widthVariation);
                    Blade(root+offset,yaw,height,width,Next(),Next());
                }
            }
            if (study == GrassStudy.Blade)
            {
                var bladeRandom = new System.Random(r.seed);
                Blade(Vector3.zero,0,r.height,r.width,(float)bladeRandom.NextDouble(),(float)bladeRandom.NextDouble());
            }
            else if (study == GrassStudy.Clump) Clump(Vector3.zero,0);
            else
                for (int z=0; z<r.patchCount; z++) for (int x=0; x<r.patchCount; x++)
                {
                    int index = z*r.patchCount+x;
                    var random = new System.Random(unchecked(r.seed+index*19349663));
                    var root = new Vector3(x-(r.patchCount-1)*.5f,0,z-(r.patchCount-1)*.5f)*r.patchSpacing;
                    root += new Vector3((float)random.NextDouble()-.5f,0,(float)random.NextDouble()-.5f)*r.patchSpacing*r.patchJitter;
                    Clump(root,index);
                }
            var mesh = new Mesh { name = "Grass " + study, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetUVs(1,tint); mesh.SetTriangles(triangles,0);
            mesh.SetUVs(3,wind); mesh.SetUVs(4,bladeRoots);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        internal static Texture2D Ramp(GrassRecipe r, Texture2D texture = null)
        {
            if (texture == null) texture = new Texture2D(256,1,TextureFormat.RGBA32,false) { name = "Grass blade gradient", wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[256];
            for (int i=0;i<colors.Length;i++) { colors[i] = r.gradient.Evaluate(i/255f); colors[i].a = 1; }
            texture.SetPixels(colors); texture.Apply();
            texture.filterMode = r.gradient.mode == GradientMode.Fixed ? FilterMode.Point : FilterMode.Bilinear;
            return texture;
        }
        internal static void Configure(Material mat, Texture2D ramp, GrassRecipe recipe)
        {
            mat.SetTexture("_BaseMap",Texture2D.whiteTexture); mat.SetColor("_BaseColor",Color.white);
            mat.SetTexture("_TintRamp",ramp); mat.SetFloat("_Cull",0); mat.SetFloat("_AlphaClip",1);
            if (mat.HasProperty("_TreeInstanceColor")) mat.SetColor("_TreeInstanceColor",Color.white);
            mat.SetFloat("_CanopySoftness",0); mat.SetFloat("_OcclusionStrength",recipe.ambientOcclusion);
            mat.SetFloat("_Transmission",recipe.translucency ? recipe.transmission : 0);
            mat.SetFloat("_AlphaToMask",recipe.alphaToCoverage ? 1 : 0);
            mat.SetFloat("_BladeEdgeSoftness",recipe.edgeSoftness);
            mat.SetFloat("_RibbonEdges",recipe.thickness <= 0 ? 1 : 0);
            mat.SetVector("_GrassWindWave",new Vector4(recipe.windWaveSize,recipe.windWaveSpeed,recipe.windWaveBreakup,recipe.windBladeVariation));
            mat.SetVector("_GrassWindResponse",new Vector4(1,1,0,0)); mat.SetFloat("_GrassWindEnabled",1);
            mat.mainTextureScale = Vector2.one; mat.mainTextureOffset = Vector2.zero;
            TreePreview.ConfigureSurface(mat,recipe.roughness,recipe.specularHighlights,recipe.environmentReflections);
        }
    }
}
