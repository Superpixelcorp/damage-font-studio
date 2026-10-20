using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;
using Mode = DamageFontStudio.Editor.DamageFontStudioWindow.PreviewMode;

namespace DamageFontStudio.Editor
{
    public static class StudioComparisonVerification
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static int assertions;

        [MenuItem("Tools/Damage Font Studio/Run Comparison Checks")]
        public static void Run()
        {
            assertions = 0;
            int baseline = DOTween.TotalActiveTweens();
            var a = Preset(.6f, Color.cyan);
            var b = Preset(1.2f, Color.yellow);
            var invalid = Preset(1, Color.white);
            invalid.segments.Add(new DamageSegment { duration = 2 });
            var window = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
            var list = Get<List<DamageFontPreset>>(window,"comparisonPresets");
            string aBefore = EditorJsonUtility.ToJson(a), bBefore = EditorJsonUtility.ToJson(b);
            try
            {
                list.Add(a); list.Add(b); list.Add(null); list.Add(invalid);
                Set(window,"hits",5); Set(window,"interval",.12f);
                Set(window,"randomizeSpawn",true); Set(window,"scatter",new Vector2(30,20));
                window.SetComparisonMode(true);
                foreach (Mode mode in Enum.GetValues(typeof(Mode)))
                foreach (bool repeat in new[] { false, true })
                {
                    Set(window,"loop",repeat); Set(window,"speed",2f);
                    window.StartPreview(mode);
                    Frame(window,0,mode);
                    Check(Cells(window).Count == 4 && !Cells(window)[2].Ready && !Cells(window)[3].Ready,"Empty and invalid cells isolated");
                    window.Advance(.1f);
                    Near(Get<float>(window,"time"),.2f,"Shared speed");
                    window.TogglePause(); window.Advance(10);
                    Near(Get<float>(window,"time"),.2f,"Shared pause");
                    window.TogglePause(); window.Advance(.05f);
                    Near(Get<float>(window,"time"),.3f,"Shared resume");
                    window.SeekPreview(.25f);
                    var poses = Snapshot(window);
                    window.SeekPreview(Get<float>(window,"length"));
                    Frame(window,Get<float>(window,"length"),mode);
                    window.SeekPreview(.25f);
                    Same(poses,Snapshot(window),"Reverse seek deterministic");
                    float shortEnd = Cells(window)[0].Duration;
                    window.SeekPreview(shortEnd+.02f);
                    Check(Cells(window)[0].Roots.All(go=>!go.activeSelf),"Short preset ends independently");
                    Check(Cells(window)[1].Roots.Any(go=>go.activeSelf),"Long preset continues");
                    window.SeekPreview(Get<float>(window,"length")-.01f);
                    window.TogglePause(); window.Advance(.02f);
                    if (repeat) { Near(Get<float>(window,"time"),.03f,"Repeat uses longest duration"); Frame(window,.03f,mode); }
                    else { Check(!Get<bool>(window,"running"),"Completion stops all panels"); window.TogglePause(); Frame(window,0,mode); }
                    window.StopPreview();
                    Check(Cells(window).Count == 0 && Get<float>(window,"length") == 0,"Stop clears all panel scenes");
                    Check(list.Count == 4 && Get<float>(window,"interval") == .12f,"Stop preserves registrations and interval");
                    Check(DOTween.TotalActiveTweens() == baseline,"No tweens after stop");
                }

                // All ordered transitions and live registration changes.
                foreach (Mode from in Enum.GetValues(typeof(Mode)))
                foreach (Mode to in Enum.GetValues(typeof(Mode)))
                {
                    window.StartPreview(from); window.SeekPreview(.2f); window.StartPreview(to);
                    Frame(window,0,to);
                }
                window.StartPreview(Mode.Series); window.SeekPreview(.2f);
                Set(window,"hits",8); Set(window,"interval",.2f); window.RebuildPreview();
                Frame(window,.2f,Mode.Series);
                Check(!Get<bool>(window,"running"),"Editing pauses comparison");
                var before = Snapshot(window); window.RebuildPreview();
                Same(before,Snapshot(window),"Seed identical across rebuilds");
                list.Insert(1,a); window.RebuildPreview();
                Check(Cells(window).Count==5 && Cells(window)[1].Preset==a,"Add duplicate while paused");
                list.RemoveAt(0); window.RebuildPreview();
                Check(Cells(window).Count==4 && Cells(window)[0].Preset==a,"Remove keeps order");
                var savedList = list.ToArray();
                list.Clear(); window.RebuildPreview();
                Check(Cells(window).Count==0 && Get<float>(window,"length")==0,"Removing all releases scenes");
                Check(DOTween.TotalActiveTweens()==baseline,"Removing all releases tweens");
                list.AddRange(savedList); window.StartPreview(Mode.Single);
                window.SetComparisonMode(false); window.StartPreview(Mode.Series);
                Check(Cells(window).Count==0 && Get<int>(window,"count")==8,"Switch to regular preview");
                window.SetComparisonMode(true); window.StartPreview(Mode.Single);
                Check(list.Count==4 && Cells(window).Count==4,"Switch back preserves registration");
                CheckRecordedTransition(window,a);
                CheckPrefab(window,a,b);
                CheckRendering(a,b);
                Check(EditorJsonUtility.ToJson(a)==aBefore && EditorJsonUtility.ToJson(b)==bBefore,"Presets remain unchanged");
                CheckLayout();
                window.StopPreview();
                Check(DOTween.TotalActiveTweens()==baseline,"Final cleanup");
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/StudioComparisonChecks.txt",$"PASS: {assertions} assertions; independent panels; mixed durations; all playback modes and transitions; repeat/pause/speed/seek; add/remove/reorder/empty/invalid presets; reproducible spread; original preset and prefab preservation; RecordedHits transitions; 1..25-panel layouts; cleanup.\n");
                Debug.Log(File.ReadAllText("Logs/StudioComparisonChecks.txt"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(invalid);
            }
            Check(DOTween.TotalActiveTweens()==baseline,"Window destruction cleanup");
        }

        static void CheckRecordedTransition(DamageFontStudioWindow window, DamageFontPreset preset)
        {
            var sequence = ScriptableObject.CreateInstance<DamageFontSequence>();
            var sample = new GameObject("Comparison recorded fixture",typeof(TextMeshPro),typeof(DamageFontObject)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var text = sample.GetComponent<TextMeshPro>(); text.font = preset.font;
                sample.GetComponent<DamageFontObject>().preset=preset;
                sequence.sourceWidth=800; sequence.sourceHeight=500; sequence.duration=1;
                sequence.events.Add(new DamageFontSequenceEvent { sample=sample,time=0,observedText="123" });
                Set(window,"recordedSequence",sequence);
                window.SelectPlaybackTarget(true);
                typeof(DamageFontStudioWindow).GetMethod("BuildRecorded",Fields).Invoke(window,new object[]{true});
                Check(Cells(window).Count==0 && Get<DamageFontSequencePlayback>(window,"recordedPlayback")!=null,"Comparison to RecordedHits");
                window.SetComparisonMode(true); window.StartPreview(Mode.Single);
                Check(Get<DamageFontSequencePlayback>(window,"recordedPlayback")==null && Cells(window).Any(c=>c.Ready),"RecordedHits to comparison");
            }
            finally { window.StopPreview(); UnityEngine.Object.DestroyImmediate(sample); UnityEngine.Object.DestroyImmediate(sequence); }
        }

        static void CheckPrefab(DamageFontStudioWindow window, DamageFontPreset a, DamageFontPreset b)
        {
            var source = new GameObject("Comparison source",typeof(TextMeshPro),typeof(DamageFontObject)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var text = source.GetComponent<TextMeshPro>(); text.font=a.font; text.text="ORIGINAL";
                text.color=Color.red; source.transform.localScale = new Vector3(2,3,1);
                var binding=source.GetComponent<DamageFontObject>(); binding.numberText=text;
                Set(window,"previewSource",source);
                var list=Get<List<DamageFontPreset>>(window,"comparisonPresets"); list.Clear(); list.Add(a); list.Add(b);
                window.StartPreview(Mode.Burst); window.SeekPreview(.2f); window.SeekPreview(0);
                Check(Cells(window).All(c=>c.Roots.Count==50),"Prefab cloned per panel and hit");
                Check(text.text=="ORIGINAL" && text.color==Color.red && source.activeSelf && source.transform.localScale==new Vector3(2,3,1),"Original prefab/object unchanged");
                window.StopPreview(); Set(window,"previewSource",null);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        static void CheckLayout()
        {
            foreach(var area in new[]{new Rect(10,20,600,400),new Rect(10,20,1200,600),new Rect(10,20,420,900)})
            for(int n=1;n<=25;n++) foreach(int columns in new[]{0,1,2,5})
            {
                var rects=DamageFontStudioWindow.ComparisonRects(area,n,columns);
                Check(rects.Length==n,"One tile per preset");
                foreach(var rect in rects)
                    Check(rect.width>0 && rect.height>0 && rect.xMin>=area.xMin-.001f && rect.xMax<=area.xMax+.001f && rect.yMin>=area.yMin-.001f && rect.yMax<=area.yMax+.001f,"Tile stays inside preview");
                for(int i=0;i<n;i++) for(int j=i+1;j<n;j++) Check(!rects[i].Overlaps(rects[j]),"Tiles never overlap");
            }
        }

        static void CheckRendering(DamageFontPreset a, DamageFontPreset b)
        {
            using (var left = new DamageComparisonPreview(a,null,false,"123456",1,0,Vector2.zero,Vector2.zero,17))
            using (var right = new DamageComparisonPreview(b,null,false,"123456",1,0,Vector2.zero,Vector2.zero,17))
            {
                var output = new Texture2D(1024,320,TextureFormat.RGB24,false);
                var previous = RenderTexture.active;
                try
                {
                    var cells = new[] { left,right };
                    for(int i=0;i<cells.Length;i++)
                    {
                        cells[i].Seek(.25f);
                        var render=(RenderTexture)cells[i].Render(new Rect(0,0,512,320),false,1,new Vector2(800,500));
                        RenderTexture.active=render;
                        var tile=new Texture2D(512,320,TextureFormat.RGB24,false);
                        try
                        {
                            tile.ReadPixels(new Rect(0,0,512,320),0,0); tile.Apply();
                            var pixels=tile.GetPixels(); int visible=0; double red=0,blue=0;
                            foreach(var p in pixels) if(p.maxColorComponent>.3f) { visible++; red+=p.r;blue+=p.b; }
                            Check(visible>50,"Each comparison panel renders text");
                            Check(i==0?blue>red*2:red>blue*2,"Panel cameras isolate preset colors");
                            output.SetPixels(i*512,0,512,320,pixels);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(tile); }
                    }
                    output.Apply(); Directory.CreateDirectory("Logs");
                    File.WriteAllBytes("Logs/ComparisonRender.png",output.EncodeToPNG());
                }
                finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(output); }
            }
        }

        static DamageFontPreset Preset(float duration,Color color)
        {
            var p=ScriptableObject.CreateInstance<DamageFontPreset>(); p.font=Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            p.initialColor=color; p.fontSize=64;
            p.segments.Add(new DamageSegment { duration=duration,targetVector=new Vector3(80,100,0),ease=Ease.Linear }); return p;
        }
        static List<DamageComparisonPreview> Cells(DamageFontStudioWindow w)=>Get<List<DamageComparisonPreview>>(w,"comparisonCells");
        static void Frame(DamageFontStudioWindow w,float time,Mode mode)
        {
            int count=mode==Mode.Burst?50:mode==Mode.Series?Get<int>(w,"hits"):1;
            float interval=mode==Mode.Burst?0:Get<float>(w,"interval");
            foreach(var cell in Cells(w).Where(c=>c.Ready))
            {
                Check(cell.Roots.Count==count,"Instances per panel");
                for(int i=0;i<count;i++) Check(cell.Roots[i].activeSelf==(time<cell.Duration && time-i*interval>=0 && time-i*interval<cell.Preset.Duration),"Per-panel visibility");
            }
        }
        static Vector3[] Snapshot(DamageFontStudioWindow w)=>Cells(w).Where(c=>c.Ready).SelectMany(c=>c.Roots).Select(go=>go.transform.localPosition).ToArray();
        static void Same(Vector3[] a,Vector3[] b,string label){Check(a.Length==b.Length,label);for(int i=0;i<a.Length;i++)Check(Vector3.Distance(a[i],b[i])<.001f,label);}
        static T Get<T>(DamageFontStudioWindow w,string name)=>(T)typeof(DamageFontStudioWindow).GetField(name,Fields).GetValue(w);
        static void Set(DamageFontStudioWindow w,string name,object value)=>typeof(DamageFontStudioWindow).GetField(name,Fields).SetValue(w,value);
        static void Near(float a,float b,string message)=>Check(Mathf.Abs(a-b)<.0001f,message);
        static void Check(bool value,string message){assertions++;if(!value)throw new Exception("Comparison: "+message);}
    }
}
