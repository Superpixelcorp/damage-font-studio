using System;
using System.IO;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoExistingPrefabVerification
    {
        static void Check(bool value, string message) { if (!value) throw new Exception("Existing game prefab: " + message); }
        static readonly Color GameColor = new Color(.2f, .7f, .9f, .8f);
        static GameObject Fixture(out TMP_Text text)
        {
            var root = new GameObject("Existing game damage fixture", typeof(TextMeshPro));
            text = root.GetComponent<TMP_Text>();
            text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            text.fontSize = 37; text.fontStyle = FontStyles.Underline; text.color = GameColor; text.text = "GAME";
            root.transform.localScale = new Vector3(2, 3, 1); root.transform.localRotation = Quaternion.Euler(0, 0, 13);
            return root;
        }
        static void CheckAppearance(TMP_Text text, TMP_FontAsset font, Material material)
        {
            Check(text.font == font && text.fontSharedMaterial == material && Mathf.Abs(text.fontSize - 37) < .0001f && text.fontStyle == FontStyles.Underline,
                "The game's font, shared material, style and base font size must remain intact.");
            Check(Mathf.Abs(text.color.r - GameColor.r) < .0001f && Mathf.Abs(text.color.g - GameColor.g) < .0001f && Mathf.Abs(text.color.b - GameColor.b) < .0001f,
                "Only opacity may change the game's authored text color.");
        }
        public static void RunChecks()
        {
            int baseline = DOTween.TotalActiveTweens();
            var sample = ScriptableObject.CreateInstance<DamageFontPreset>();
            sample.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            sample.fontMaterial = sample.font.material; sample.fontSize = 180; sample.overrideFontStyle = true; sample.fontStyle = FontStyles.Bold | FontStyles.Italic;
            sample.initialColor = Color.yellow; sample.initialScale = new Vector3(.4f, 1, 1);
            sample.segments.Add(new DamageSegment { channel = DamageChannel.Position, duration = 1, targetVector = new Vector3(60, 100, 0), ease = Ease.Linear });
            sample.segments.Add(new DamageSegment { channel = DamageChannel.Scale, duration = 1, targetVector = new Vector3(.8f, 1.5f, 1), ease = Ease.Linear });
            sample.segments.Add(new DamageSegment { channel = DamageChannel.Opacity, duration = 1, targetValue = 0, ease = Ease.Linear });
            sample.segments.Add(new DamageSegment { channel = DamageChannel.Color, duration = 1, targetColor = Color.magenta, ease = Ease.Linear });
            var motion = DamageVideoSampleExporter.MotionOnly(sample);
            var root = Fixture(out var text); var font = text.font; var material = text.fontSharedMaterial;
            var sequence = ScriptableObject.CreateInstance<DamageFontSequence>(); var parent = new GameObject("Existing game prefab sequence");
            try
            {
                Check(motion.animationOnly && !motion.font && !motion.fontMaterial && !motion.overrideFontStyle && motion.initialColor == Color.white,
                    "Game motion assets must carry no sample font/material/style/tint.");
                Check(motion.initialScale == Vector3.one && motion.segments.All(s => s.channel != DamageChannel.Color),
                    "Sample glyph fitting and RGB color channels must not enter game motion.");
                foreach (bool overrideStyle in new[] { false, true })
                using (var player = new DamageFontPlayback(root, text, overrideTextAppearance: overrideStyle))
                foreach (string number in new[] { "1", "9876543210" })
                {
                    player.Prepare(motion, number, new Vector3(10, 20, 0), manual: true);
                    foreach (float time in new[] { 0f, .25f, .7f, .1f, .9f })
                    {
                        player.Seek(time); CheckAppearance(text, font, material);
                        Check(text.text == number, "Arbitrary damage values remain editable text.");
                        Check(Vector3.Distance(root.transform.localPosition, new Vector3(10 + 60 * time, 20 + 100 * time, 0)) < .001f, "Only extracted relative movement is applied at the game's spawn point.");
                        Check(Vector3.Distance(root.transform.localScale, new Vector3(2 * (1 + time), 3 * (1 + .5f * time), 1)) < .001f, "Relative animation scale preserves the game's authored base size.");
                        Check(Quaternion.Angle(root.transform.localRotation, Quaternion.Euler(0, 0, 13)) < .001f, "The game's base rotation is preserved.");
                        Check(Mathf.Abs(text.color.a - GameColor.a * (1 - time)) < .001f, "Extracted fade is multiplied into the game's own alpha.");
                    }
                }
                sequence.duration = 1; sequence.events.Add(new DamageFontSequenceEvent { motionPreset = motion, observedText = "1234", position = new Vector3(10, 20, 0) });
                // No generated sample prefab and no DamageFontObject component are needed.
                using (var playback = new DamageFontSequencePlayback(sequence, parent.transform, targetPrefab: root))
                {
                    playback.Seek(.25f); var clone = parent.GetComponentInChildren<TMP_Text>(true);
                    CheckAppearance(clone, font, material); Check(clone.text == "1234", "Whole sequence reuses the game's prefab and per-hit motion assets.");
                }
                Check(text.text == "GAME" && root.transform.localScale == new Vector3(2, 3, 1), "The original game object is restored and sequence spawning does not modify its prefab.");
                Check(sample.font && sample.fontMaterial && sample.initialScale.x == .4f && sample.initialColor == Color.yellow,
                    "Producing a game motion asset must not modify the comparison sample preset.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(sequence);
                UnityEngine.Object.DestroyImmediate(motion); UnityEngine.Object.DestroyImmediate(sample);
            }
            Check(DOTween.TotalActiveTweens() == baseline, "Owned tweens are released.");
            Directory.CreateDirectory("Logs/ExistingPrefabChecks");
            File.WriteAllText("Logs/ExistingPrefabChecks/model.txt", "PASS: game-owned font/material/style/RGB/base size/rotation preserved; sample glyph fitting removed; arbitrary digits; extracted position/relative scale/fade; reverse seeking; complete recorded sequence uses a game prefab without generated sample dependency; original object restored; tween cleanup.\n");
        }
        public static void VerifySequence(DamageFontSequence sequence)
        {
            var prefab = Fixture(out var text); var font = text.font; var material = text.fontSharedMaterial;
            var parent = new GameObject("Actual recorded sequence with existing game prefab");
            try
            {
                foreach (var hit in sequence.events)
                {
                    Check(hit.motionPreset && hit.motionPreset.animationOnly && !hit.motionPreset.font && !hit.motionPreset.fontMaterial && hit.motionPreset.initialColor == Color.white && hit.motionPreset.initialScale == Vector3.one,
                        "Every exported hit must have a standalone animation-only preset.");
                    var path = AssetDatabase.GetAssetPath(hit.motionPreset);
                    Check(!AssetDatabase.GetDependencies(path).Any(p => p.EndsWith(".prefab") || p.EndsWith(".mat")), "Game motion asset must not depend on a generated sample prefab or material.");
                }
                using (var player = new DamageFontSequencePlayback(sequence, parent.transform, "9876543210", targetPrefab: prefab))
                foreach (float time in new[] { 0f, .1f, .4f, .7f, .2f })
                {
                    player.Seek(time);
                    Check(parent.transform.childCount == sequence.events.Count, "Every recorded hit uses the supplied game prefab.");
                    foreach (var clone in parent.GetComponentsInChildren<TMP_Text>(true))
                    { CheckAppearance(clone, font, material); Check(clone.text == "9876543210", "All recorded events accept game damage values."); }
                }
                Directory.CreateDirectory("Logs/ExistingPrefabChecks");
                File.WriteAllText("Logs/ExistingPrefabChecks/reference.txt", "PASS: actual source range exported animation-only assets; no sample prefab/material dependencies; whole recorded sequence applied to an independently authored game prefab while preserving appearance.\n");
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(prefab); }
        }
    }
}
