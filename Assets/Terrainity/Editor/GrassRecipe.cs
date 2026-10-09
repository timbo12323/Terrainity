using System;
using System.IO;
using UnityEngine;

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class GrassRecipe
    {
        public string kind = "TerrainityGrass";
        public int version = 1;
        public string assetName = "Meadow_Grass", style = "Meadow";
        public int seed = 12345;
        public float height = .55f, width = .035f, thickness;
        public float rootWidth = .65f, tipWidth = 0, taper = 1.4f;
        public float belly = .25f, bellyPosition = .4f;
        public float lean = 12, bend = .3f, bendStart = .15f, twist = 15, fold = 12;
        public int lengthSegments = 6, widthSegments = 2;
        public int bladeCount = 24;
        public float radius = .18f, gap = 0, irregularity = .65f;
        public float heightVariation = .2f, widthVariation = .12f, directionVariation = .7f;
        public int patchCount = 3;
        public float patchSpacing = .38f, patchJitter = .3f;
        public Gradient gradient = DefaultGradient(false);
        public bool translucency, alphaToCoverage, specularHighlights, environmentReflections;
        public float transmission = .5f, ambientOcclusion = .5f, roughness = 1, edgeSoftness;
        public float windWaveSize = .6f, windWaveSpeed = .45f, windWaveBreakup = .65f, windBladeVariation = .25f;
        internal static readonly string[] Styles = { "Short lawn", "Meadow", "Broad stylized", "Dry grass" };

        internal static Gradient DefaultGradient(bool dry)
        {
            var g = new Gradient();
            g.SetKeys(new[] {
                new GradientColorKey(dry ? new Color(.23f,.18f,.07f) : new Color(.07f,.17f,.035f), 0),
                new GradientColorKey(dry ? new Color(.55f,.41f,.16f) : new Color(.25f,.43f,.07f), .55f),
                new GradientColorKey(dry ? new Color(.85f,.72f,.4f) : new Color(.58f,.7f,.23f), 1)
            }, new[] { new GradientAlphaKey(1,0), new GradientAlphaKey(1,1) });
            return g;
        }

        internal void ApplyStyle(string value)
        {
            style = value;
            height = .55f; width = .035f; thickness = 0;
            rootWidth = .65f; tipWidth = 0; taper = 1.4f; belly = .25f; bellyPosition = .4f;
            lean = 12; bend = .3f; bendStart = .15f; twist = 15; fold = 12;
            if (value == "Short lawn") { height = .15f; width = .012f; rootWidth = 1; belly = 0; taper = 2; lean = 5; bend = .08f; twist = 0; fold = 5; }
            if (value == "Broad stylized") { height = .4f; width = .075f; rootWidth = .45f; belly = .65f; taper = 2; bellyPosition = .35f; bend = .4f; fold = 22; }
            if (value == "Dry grass") { height = .7f; width = .018f; rootWidth = 1; belly = .1f; taper = .8f; lean = 20; bend = .55f; bendStart = .4f; twist = 35; fold = 30; }
            gradient = DefaultGradient(value == "Dry grass");
        }

        internal GrassRecipe Copy()
        {
            var copy = JsonUtility.FromJson<GrassRecipe>(JsonUtility.ToJson(this));
            copy.gradient = TreeGradientJson.From(gradient)?.ToGradient() ?? DefaultGradient(false);
            return copy;
        }

        internal void Validate()
        {
            if (kind != "TerrainityGrass" || version != 1) throw new InvalidDataException("Choose a Terrainity grass recipe, version 1.");
            foreach (float v in new[] { height,width,thickness,rootWidth,tipWidth,taper,belly,bellyPosition,lean,bend,bendStart,twist,fold,radius,gap,irregularity,heightVariation,widthVariation,directionVariation,patchSpacing,patchJitter,transmission,ambientOcclusion,roughness,edgeSoftness,windWaveSize,windWaveSpeed,windWaveBreakup,windBladeVariation })
                if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("Grass settings must be finite numbers.");
            height = Mathf.Clamp(height,.03f,3); width = Mathf.Clamp(width,.002f,.3f); thickness = Mathf.Clamp(thickness,0,.02f);
            rootWidth = Mathf.Clamp(rootWidth,.05f,1); tipWidth = Mathf.Clamp01(tipWidth); taper = Mathf.Clamp(taper,.2f,5);
            belly = Mathf.Clamp01(belly); bellyPosition = Mathf.Clamp(bellyPosition,.05f,.95f);
            lean = Mathf.Clamp(lean,-60,60); bend = Mathf.Clamp(bend,-1.5f,1.5f); bendStart = Mathf.Clamp(bendStart,0,.9f);
            twist = Mathf.Clamp(twist,-180,180); fold = Mathf.Clamp(fold,0,75);
            lengthSegments = Mathf.Clamp(lengthSegments,1,24); widthSegments = Mathf.Clamp(widthSegments,1,8);
            bladeCount = Mathf.Clamp(bladeCount,1,128); radius = Mathf.Clamp(radius,.01f,1); gap = Mathf.Clamp01(gap);
            irregularity = Mathf.Clamp01(irregularity); heightVariation = Mathf.Clamp(heightVariation,0,.8f); widthVariation = Mathf.Clamp(widthVariation,0,.8f);
            directionVariation = Mathf.Clamp01(directionVariation); patchCount = Mathf.Clamp(patchCount,1,5);
            patchSpacing = Mathf.Clamp(patchSpacing,.02f,2); patchJitter = Mathf.Clamp01(patchJitter);
            transmission = Mathf.Clamp01(transmission); ambientOcclusion = Mathf.Clamp01(ambientOcclusion);
            roughness = Mathf.Clamp01(roughness); edgeSoftness = Mathf.Clamp(edgeSoftness,0,.25f);
            windWaveSize = Mathf.Clamp(windWaveSize,.05f,5); windWaveSpeed = Mathf.Clamp(windWaveSpeed,0,2);
            windWaveBreakup = Mathf.Clamp01(windWaveBreakup); windBladeVariation = Mathf.Clamp(windBladeVariation,0,.8f);
            if (Array.IndexOf(Styles,style) < 0) style = "Meadow";
            gradient = gradient ?? DefaultGradient(false);
            TreeGradientJson.From(gradient).ToGradient();
        }
    }

    [Serializable]
    internal sealed class GrassRecipeJson
    {
        public string kind = "TerrainityGrass";
        public int version = 1;
        public GrassRecipe settings;
        public TreeGradientJson gradient;
        internal static GrassRecipeJson From(GrassRecipe r) => new GrassRecipeJson { settings = r.Copy(), gradient = TreeGradientJson.From(r.gradient) };
        internal static GrassRecipe Read(string path)
        {
            string json = TreeJsonStorage.ReadText(path);
            var doc = JsonUtility.FromJson<GrassRecipeJson>(json);
            if (doc == null || doc.kind != "TerrainityGrass" || doc.version != 1 || doc.settings == null || doc.gradient == null)
                throw new InvalidDataException("Choose a Terrainity grass recipe JSON file, version 1.");
            if (!json.Contains("\"windWaveSize\"")) doc.settings.windWaveSize = .6f;
            if (!json.Contains("\"windWaveSpeed\"")) doc.settings.windWaveSpeed = .45f;
            if (!json.Contains("\"windWaveBreakup\"")) doc.settings.windWaveBreakup = .65f;
            if (!json.Contains("\"windBladeVariation\"")) doc.settings.windBladeVariation = .25f;
            doc.settings.gradient = doc.gradient.ToGradient(); doc.settings.Validate(); return doc.settings;
        }
    }
}
