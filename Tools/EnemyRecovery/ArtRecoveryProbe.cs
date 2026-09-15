using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed partial class RecoveryProbe
{
    // The original player has the actual compiled palette shader. Capture its
    // output instead of treating AssetRipper's dummy shader as original code.
    IEnumerator CaptureArt()
    {
        phase="art-palette-capture";
        yield return null;
        var records=new List<object>();
        var plan=JObject.Parse(File.ReadAllText(Arg("--recovery-art-manifest=","")));
        var textures=Resources.FindObjectsOfTypeAll<Texture2D>();
        var shader=Shader.Find("AstralShift/PaletteSwapBlitShader");
        if(shader==null){File.WriteAllText(Path.Combine(Output,"art.failed"),"Original palette shader missing");yield break;}
        var material=new Material(shader);
        foreach(var row in plan["bakes"])
        {
            string textureName=(string)row["textureName"], lutName=(string)row["lutName"], file=(string)row["file"];
            try
            {
                var source=textures.First(t=>t.name==textureName);
                var lut=textures.First(t=>t.name==lutName);
                var rt=RenderTexture.GetTemporary(source.width,source.height,0,source.graphicsFormat);
                var before=RenderTexture.active;
                Texture2D outputTexture=null;
                try
                {
                    material.SetTexture("_ColorLookupTex",lut);
                    Graphics.Blit(source,rt,material);
                    RenderTexture.active=rt;
                    outputTexture=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);
                    outputTexture.ReadPixels(new Rect(0,0,source.width,source.height),0,0);
                    outputTexture.Apply(false,false);
                    File.WriteAllBytes(Path.Combine(Output,file),outputTexture.EncodeToPNG());
                    records.Add(new{textureName=textureName,lutName=lutName,file=file,width=source.width,height=source.height,
                        format=source.graphicsFormat.ToString(),activeColorSpace=QualitySettings.activeColorSpace.ToString(),shader=shader.name});
                }
                finally{RenderTexture.active=before;RenderTexture.ReleaseTemporary(rt);if(outputTexture!=null)Object.Destroy(outputTexture);}
            }
            catch(Exception e){Log("art-error",new{texture=textureName,lut=lutName,error=e.ToString()});File.WriteAllText(Path.Combine(Output,"art.failed"),e.ToString());}
            yield return null;
        }
        Object.Destroy(material);
        File.WriteAllText(Path.Combine(Output,"art-palettes.json"),JsonConvert.SerializeObject(records,Formatting.Indented));
        File.WriteAllText(Path.Combine(Output,"art.complete"),records.Count.ToString());
        yield return new WaitForSecondsRealtime(1);
        Application.Quit();
    }
}
