using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Editor-only preference: never stored in a preset or used by runtime playback.
    internal static class StudioLanguage
    {
        const string Key = "DamageFontStudio.Editor.Language";
        static readonly string[] Languages = { "한국어", "English" };
        static readonly Dictionary<string, string> EaseShapes = new Dictionary<string, string>
        {
            {"Linear", "선형"}, {"Sine", "사인"}, {"Quad", "2차"}, {"Cubic", "3차"},
            {"Quart", "4차"}, {"Quint", "5차"}, {"Expo", "지수"}, {"Circ", "원형"},
            {"Elastic", "탄성"}, {"Back", "되튕김"}, {"Bounce", "바운스"}, {"Unset", "미설정"},
            {"Flash", "플래시"}, {"INTERNAL_Zero", "내부: 0"}, {"INTERNAL_Custom", "내부: 사용자 정의"}
        };
        public static bool Korean => EditorPrefs.GetInt(Key, 0) == 0;
        public static string Text(string key)
            => key != null && Korean && KoreanText.TryGetValue(key, out var value) ? value : key;
        public static string Format(string key, params object[] args) => string.Format(Text(key), args);

        public static void DrawSelector()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Korean ? "언어" : "Language", GUILayout.Width(Korean ? 28 : 60));
                int current = Korean ? 0 : 1;
                int next = EditorGUILayout.Popup(current, Languages, GUILayout.Width(90));
                if (next != current)
                {
                    EditorPrefs.SetInt(Key, next);
                    foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) window.Repaint();
                    // Avoid triggering a surrounding property change check when only the language changed.
                    GUIUtility.ExitGUI();
                }
            }
        }

        public static GUIContent Label(SerializedProperty property)
        {
            string tooltip = property.tooltip;
            return new GUIContent(Text(property.displayName), Text(tooltip));
        }

        public static string EnumText(string name)
        {
            if (!Korean) return ObjectNames.NicifyVariableName(name);
            if (KoreanText.TryGetValue(name, out var translated)) return translated;
            // DOTween easing names retain their stable enum values; only labels change.
            string compact = name.Replace(" ", "");
            string prefix = compact.StartsWith("InOut") ? "InOut" : compact.StartsWith("Out") ? "Out" : compact.StartsWith("In") ? "In" : "";
            string shape = compact.Substring(prefix.Length);
            return EaseShapes.TryGetValue(shape, out var value) ? value + (prefix == "InOut" ? " (가속·감속)" : prefix == "In" ? " (가속)" : prefix == "Out" ? " (감속)" : "") : name;
        }

        public static void Property(SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.Enum)
            {
                Rect rect = EditorGUILayout.GetControlRect();
                Property(rect, property);
            }
            else if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, Label(property), true);
                if (!property.isExpanded) return;
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                int size = EditorGUILayout.IntField(Korean ? "구간 수" : "Size", property.arraySize);
                if (EditorGUI.EndChangeCheck()) property.arraySize = Mathf.Max(0, size);
                for (int i = 0; i < property.arraySize; i++) EditorGUILayout.PropertyField(property.GetArrayElementAtIndex(i), true);
                EditorGUI.indentLevel--;
            }
            else EditorGUILayout.PropertyField(property, Label(property), true);
        }

        public static void Property(Rect rect, SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Enum)
            { EditorGUI.PropertyField(rect, property, Label(property), true); return; }
            EditorGUI.BeginProperty(rect, Label(property), property);
            bool mixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            if (property.name == "fontStyle")
            {
                EditorGUI.BeginChangeCheck();
                var style = (TMPro.FontStyles)EditorGUI.EnumFlagsField(rect, Label(property), (TMPro.FontStyles)property.intValue);
                if (EditorGUI.EndChangeCheck()) property.intValue = (int)style;
                EditorGUI.showMixedValue = mixed; EditorGUI.EndProperty(); return;
            }
            var names = Array.ConvertAll(property.enumNames, EnumText);
            EditorGUI.BeginChangeCheck();
            int next = EditorGUI.Popup(rect, Label(property).text, property.enumValueIndex, names);
            if (EditorGUI.EndChangeCheck()) property.enumValueIndex = next;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.EndProperty();
        }

        static readonly Dictionary<string, string> KoreanText = new Dictionary<string, string>
        {
            { "Multiple preset comparison", "여러 프리셋 비교" },
            { "Comparison presets", "비교 프리셋" },
            { "Add presets below or drag several preset assets here. All tiles share playback controls; shorter effects wait until the longest finishes.", "아래 목록에 프리셋을 추가하거나 여러 에셋을 드래그하세요. 모든 영역을 함께 재생하며, 짧은 연출은 가장 긴 연출이 끝날 때까지 대기합니다." },
            { "Add preset slot", "프리셋 슬롯 추가" },
            { "Add current preset", "현재 프리셋 추가" },
            { "Edit", "편집" },
            { "Grid columns", "분할 열 수" },
            { "Automatic", "자동" },
            { "{0} presets", "프리셋 {0}개" },
            { "Comparison view size", "비교 영역 크기" },
            { "Empty slot", "빈 슬롯" },
            { "{0:0.00} / {1:0.00} s", "{0:0.00} / {1:0.00} 초" },
            { "Select presets and press Single / Hit series.", "프리셋을 선택하고 단발 / 연타를 누르세요." },
            { "Add presets to compare them in separate tiles.", "프리셋을 추가하면 각 영역에서 나란히 비교할 수 있습니다." },
            { "Play with default digits (no prefab)", "기본 숫자로 재생 (프리팹 불필요)" },
            { "Choose a motion preset to preview.", "재생할 연출 프리셋을 선택하세요." },
            { "Playback finished. Press Single / Hit series again, or enable Repeat and Resume.", "재생이 끝났습니다. 단발·연타를 다시 누르거나 반복을 켜고 재개하세요." },
            { "Font Material", "폰트 머티리얼" },
            { "Override Font Style", "글꼴 스타일 적용" },
            { "Font Style", "글꼴 스타일" },
            { "Automatic sample creation", "영상에서 샘플 자동 생성" },
            { "Load a video, preview it and choose Start / End. Start analysis then detects the effects and creates editable presets + sample prefabs.", "영상을 불러와 미리보기에서 시작·종료를 지정하세요. 분석 시작을 누르면 연출을 분석하고 프리셋과 샘플 프리팹을 자동 생성합니다." },
            { "Analyze to (seconds)", "분석 종료 (초)" },
            { "Start analysis → create samples", "분석 시작 → 샘플 자동 생성" },
            { "Selected range: {0:0.000}s · frames {1}–{2}", "선택 구간: {0:0.000}초 · 프레임 {1}–{2}" },
            { "Video ready. Preview it, choose the analysis range, then press Start analysis → create samples.", "영상을 불러왔습니다. 미리보기에서 분석 시작·종료를 지정한 뒤 분석 시작 → 샘플 자동 생성을 누르세요." },
            { "Analysis cancelled. The selected range is kept.", "분석을 취소했습니다. 선택한 구간은 유지됩니다." },
            { "Select a video to detect floating light/pink numbers and save editable presets + sample prefabs. Appearance and fade are estimates; sample text can be changed freely.", "영상을 선택하면 밝은색·분홍색 데미지 숫자를 찾아 프리셋과 샘플 프리팹을 저장합니다. 외형·페이드는 추정값이며 숫자는 자유롭게 바꿀 수 있습니다." },
            { "Analyze from (seconds)", "분석 시작 (초)" },
            { "Analyze to (0 = full video)", "분석 종료 (0 = 영상 끝)" },
            { "Select video → create samples", "영상 선택 → 샘플 자동 생성" },
            { "Cancel analysis", "분석 취소" },
            { "Analysis cancelled.", "분석을 취소했습니다." },
            { "Preview sample", "샘플 미리보기" },
            { "Compare source", "원본과 비교" },
            { "Compare recorded hits", "원본과 연타 시퀀스 비교" },
            { "Recorded hit sequence", "추출된 연타 시퀀스" },
            { "Play RecordedHits in Studio", "재생 테스트에서 전체 재생" },
            { "Play full sequence", "전체 시퀀스 재생" },
            { "RecordedHits playback", "RecordedHits 전체 재생" },
            { "Preset authoring / test", "프리셋 제작 / 테스트" },
            { "Edit recorded hit preset", "시퀀스 타격 프리셋 편집" },
            { "Hit to edit", "편집할 타격" },
            { "{0} recorded hits", "타격 {0}개" },
            { "Leave empty to use each hit's saved number.", "비워 두면 타격별 저장된 숫자를 사용합니다." },
            { "Press Play full sequence to preview RecordedHits.", "전체 시퀀스 재생을 눌러 RecordedHits를 확인하세요." },
            { "Plays the complete generated sequence with its recorded timing, samples and motion. The timeline includes the final hits fading out. No video decoder is used.", "생성된 전체 시퀀스의 타격 간격·샘플·움직임을 재생합니다. 마지막 타격이 사라질 때까지 확인할 수 있으며 원본 영상은 필요 없습니다." },
            { "Choose a RecordedHits sequence with hit events.", "타격 데이터가 있는 RecordedHits를 선택하세요." },
            { "RecordedHits has invalid source dimensions.", "RecordedHits의 기준 화면 크기가 올바르지 않습니다." },
            { "RecordedHits contains a missing sample or preset.", "RecordedHits에 연결된 샘플 또는 프리셋이 없습니다." },
            { "Manual extraction / corrections", "수동 추출 / 보정" },
            { "Generated samples", "생성된 개별 샘플" },
            { "{0} occluded hits use a measured family profile", "가려진 타격 {0}개에 관측한 같은 종류의 연출을 적용했습니다." },
            { "{0} recorded hits · {1} unread numbers", "타격 {0}개 · 숫자 판독 불가 {1}개" },
            { "Recorded hits ready. Play the range to compare with the source.", "연타 시퀀스를 불러왔습니다. 구간 재생으로 원본과 비교하세요." },
            { "Source / reconstruction", "원본 / 추출 결과" },
            { "Detecting hit events", "타격 시점 검출" },
            { "Measuring motion and fade", "이동·크기·페이드 측정" },
            { "Recovering hit onset", "등장 시점 역방향 추적" },
            { "Finding overlapping repeated numbers", "영상에서 관측한 숫자로 겹친 연타 재검출" },
            { "Randomize spawn positions", "생성 위치 무작위 분산" },
            { "Lock horizontal position", "시작 X 위치 고정" },
            { "Lock spawn X", "시작 X 위치 고정" },
            { "Remove screen shake", "화면 흔들림 제거" },
            { "Final game motion", "게임 적용 최종본" },
            { "Apply this file to the game", "게임에 연결할 파일" },
            { "Create and test GameMotion.asset", "게임 적용본 생성 후 재생 테스트" },
            { "Test final game motion", "게임 적용 최종본 재생 테스트" },
            { "Test this exact preset", "이 파일을 재생 테스트에서 확인" },
            { "Analysis hits / choose another source", "타격별 분석 결과 / 최종본 변경" },
            { "Analysis samples (advanced)", "분석용 샘플 (고급)" },
            { "Test this observation", "이 타격 연출 확인" },
            { "Use as final game motion", "이 연출을 최종본으로 지정" },
            { "Existing game damage prefab / object", "기존 게임 데미지 프리팹 / 오브젝트" },
            { "Preview this exact asset on your existing game prefab. Sample prefabs are analysis only.", "이 파일을 기존 게임 프리팹에 적용해 확인합니다. 생성된 샘플 프리팹은 분석용입니다." },
            { "Light-01, 02... are separate tracked hits, not versions. Choosing a source copies that hit to GameMotion.asset without averaging.", "Light-01, 02…는 각각 추적한 타격이며 버전 번호가 아닙니다. 선택한 타격의 연출을 평균으로 합치지 않고 GameMotion.asset에 복사합니다." },
            { "RecordedHits is an analysis comparison: each hit uses its own measured motion. Test GameMotion.asset to see the exact game-facing effect.", "RecordedHits는 타격별로 측정한 서로 다른 연출을 비교하는 분석 자료입니다. 실제 게임 적용 연출은 GameMotion.asset 재생 테스트로 확인하세요." },
            { "This is the final game asset. The playback lab uses this exact preset; assign your existing game prefab to verify its appearance.", "현재 파일이 게임에 연결할 최종본입니다. 재생 테스트는 이 파일을 그대로 사용합니다. 기존 게임 프리팹을 지정하면 실제 외형으로 확인할 수 있습니다." },
            { "No game prefab assigned: showing fallback text. Assign your existing prefab to verify the actual game appearance.", "게임 프리팹이 없어 기본 텍스트로 표시합니다. 실제 게임 외형을 확인하려면 기존 프리팹을 지정하세요." },
            { "Hit {0} · {1} · {2:0.000}s · {3:0.000}s long · {4}", "타격 {0} · {1} · 영상 {2:0.000}초 · 길이 {3:0.000}초 · {4}" },
            { "Observed end", "종료 관측" },
            { "Cut / unobserved end", "구간 잘림 / 종료 미관측" },
            { "Inferred", "가림 추정" },
            { "Limit analysis area", "분석 영역 제한" },
            { "Select analysis area", "분석 영역 지정" },
            { "Cancel area selection", "영역 지정 취소" },
            { "Use full frame", "전체 화면 사용" },
            { "Analysis area (source pixels)", "분석 영역 (원본 픽셀)" },
            { "Analysis area", "분석 영역" },
            { "Drag the source preview to select the analysis area. Include the whole motion path.", "원본 미리보기를 드래그해 분석 영역을 지정하세요. 연출의 이동 경로 전체를 포함해주세요." },
            { "Analysis area selected. Set the time range, then start analysis.", "분석 영역을 지정했습니다. 시간 구간을 설정한 뒤 분석을 시작하세요." },
            { "Only complete number boxes inside this area are analyzed. Include the full flight path; motion outside the area is excluded.", "이 영역 안에 완전히 들어온 숫자만 분석합니다. 이동 경로 전체를 포함해주세요. 영역 밖 움직임은 제외됩니다." },
            { "Select an analysis region at least 4 × 4 pixels inside the video.", "영상 안에서 가로·세로 4픽셀 이상의 분석 영역을 지정하세요." },
            { "No tracks found inside the selected area. Include the full motion path or choose a clearer range.", "선택 영역에서 연출을 찾지 못했습니다. 이동 경로 전체가 들어오도록 영역을 넓히거나 다른 시간 구간을 선택하세요." },
            { "Animation only: preserves the game's font, material, style and RGB colors.", "연출 전용: 게임 프리팹의 폰트·머티리얼·스타일·RGB 색상을 유지합니다." },
            { "Include camera motion for source comparison", "원본 비교에 화면 움직임 포함" },
            { "Spawn X", "시작 X 좌표" },
            { "All hits start at the same X. Horizontal motion after spawning is preserved.", "모든 타격의 시작 X 좌표를 맞춥니다. 시작 후 수평 이동과 곡선 궤적은 유지됩니다." },
            { "Light numbers", "일반 데미지" },
            { "Pink numbers", "강조 데미지" },
            { "Samples created. Select a sample to change its number and preview it in Studio.", "샘플을 생성했습니다. 샘플 미리보기에서 숫자를 바꾸고 재생할 수 있습니다." },
            { "Analyzing video. You can cancel without changing existing presets.", "영상을 분석 중입니다. 취소해도 기존 프리셋에는 영향이 없습니다." },
            { "Choose a valid analysis range (end 0 means the entire video).", "분석 구간을 확인하세요. 종료를 0으로 두면 영상 끝까지 분석합니다." },
            { "Choose at least 0.3 seconds for analysis.", "분석 구간을 0.3초 이상 지정하세요." },
            { "No complete floating-number tracks found. Try a clearer range or use manual extraction.", "완전한 데미지 숫자 궤적을 찾지 못했습니다. 더 선명한 구간을 지정하거나 수동 추출을 사용하세요." },
            { "DAMAGE FONT STUDIO", "데미지 폰트 스튜디오" },
            { "Damage Font Studio", "데미지 폰트 스튜디오" },
            { "Extract from video", "영상에서 추출" },
            { "Editor preview · DOTween + TextMeshPro", "에디터 미리보기 · DOTween + TextMeshPro" },
            { "Exit Play mode to use the editor preview.", "에디터 미리보기를 사용하려면 플레이 모드를 종료하세요." },
            { "Preset (.asset)", "프리셋 (.asset)" },
            { "New", "새로 만들기" },
            { "Duplicate", "복제" },
            { "Save", "저장" },
            { "Locate", "위치 찾기" },
            { "Playback lab", "재생 테스트" },
            { "Prefab / Scene Object", "프리팹 / 씬 오브젝트" },
            { "Override text style", "텍스트 스타일 덮어쓰기" },
            { "Number Text must belong to the selected object's hierarchy.", "숫자 텍스트는 선택한 오브젝트 또는 그 자식에 속해야 합니다." },
            { "Use object's preset and text binding", "오브젝트의 프리셋과 텍스트 사용" },
            { "Display number / text", "표시할 숫자 / 텍스트" },
            { "Hit count", "연타 횟수" },
            { "Hit interval (seconds)", "연타 간격 (초)" },
            { "Spawn position", "생성 위치" },
            { "Random spread (+/-)", "랜덤 분산 (±)" },
            { "Random seed", "랜덤 시드" },
            { "Light background", "밝은 배경" },
            { "Repeat", "반복" },
            { "Speed", "배속" },
            { "Single", "단발" },
            { "Hit series", "연타" },
            { "Burst × 50", "50개 동시 출력" },
            { "Pause", "일시정지" },
            { "Resume", "계속 재생" },
            { "Stop / clear", "정지 / 초기화" },
            { "Time (seconds)", "시간 (초)" },
            { "Preview zoom", "미리보기 확대" },
            { "Select a preset and press Single / Hit series", "프리셋을 선택하고 단발 / 연타를 누르세요." },
            { "Fixed random seed keeps replay and scrubbing reproducible. Preview objects never enter your scene.", "고정된 랜덤 시드로 재생·시간 이동 결과를 유지합니다. 미리보기 오브젝트는 씬에 추가되지 않습니다." },
            { "Duplicate preset", "프리셋 복제" },
            { "New preset", "새 프리셋" },
            { "Choose a preset path", "프리셋을 저장할 위치를 선택하세요." },
            { "Number Text must be a child of this animated root.", "숫자 텍스트는 애니메이션 루트 또는 그 자식에 속해야 합니다." },
            { "Preview in Damage Font Studio", "데미지 폰트 스튜디오에서 미리보기" },
            { "Play", "재생" },
            { "Cancel", "취소" },
            { "Kind", "데미지 종류" },
            { "Font", "폰트" },
            { "Font Size", "폰트 크기" },
            { "Initial Color", "초기 색상" },
            { "Initial Opacity", "초기 불투명도" },
            { "Initial Scale", "초기 크기" },
            { "Initial Rotation", "초기 회전" },
            { "Segments", "연출 구간" },
            { "Channel", "속성" },
            { "Start", "시작 시간" },
            { "Duration", "지속 시간" },
            { "Target Vector", "목표 벡터" },
            { "Target Value", "목표값" },
            { "Target Color", "목표 색상" },
            { "Position Path", "이동 경로" },
            { "Arc Height", "포물선 높이" },
            { "Use Curve", "커브 사용" },
            { "Curve", "커브" },
            { "Ease", "가속·감속" },
            { "Normal", "일반" },
            { "Critical", "크리티컬" },
            { "Incoming", "피격" },
            { "Skill", "스킬" },
            { "Position", "위치" },
            { "Scale", "크기" },
            { "Rotation", "회전" },
            { "Opacity", "불투명도" },
            { "Color", "색상" },
            { "Direct", "직선" },
            { "Parabola", "포물선" },
            { "Preset", "프리셋" },
            { "Number Text", "숫자 텍스트" },
            { "Override Text Appearance", "텍스트 스타일 덮어쓰기" },
            { "Play On Enable", "활성화 시 재생" },
            { "Number", "표시할 숫자" },
            { "Vertical lift above the straight start-to-end line at the path midpoint.", "시작점과 끝점을 잇는 직선의 중간에서 위로 솟는 높이입니다." },
            { "Optional. If empty, the first TMP text under this root displays the number.", "선택 사항입니다. 비어 있으면 루트 아래 첫 TMP 텍스트에 숫자를 표시합니다." },
            { "Start must be >= 0 and duration must be > 0 (finite values).", "시작 시간은 0 이상, 지속 시간은 0보다 큰 유한한 값이어야 합니다." },
            { "A custom curve needs at least two keys.", "사용자 커브에는 최소 두 개의 키가 필요합니다." },
            { "Arc Height must be a finite value >= 0.", "포물선 높이는 0 이상의 유한한 값이어야 합니다." },
            { "Segments on the same channel must not overlap.", "같은 속성의 연출 구간은 시간이 겹칠 수 없습니다." },
            { "Video Extraction", "영상 추출" },
            { "Open a local video to begin.", "로컬 영상을 불러와 시작하세요." },
            { "Open video, or load a session and press Reopen video.", "영상을 불러오거나 세션을 선택한 뒤 다시 열기 / 연결을 누르세요." },
            { "New extraction", "새 추출 작업" },
            { "Create a new session? Save the current session first if you need its corrections.", "새 세션을 만드시겠습니까? 현재 보정 내용을 유지하려면 먼저 세션을 저장하세요." },
            { "New session", "새 세션" },
            { "Preparing video...", "영상 준비 중..." },
            { "Video metadata differs from the saved session. Open as a new video instead.", "영상 정보가 저장된 세션과 다릅니다. 새 영상으로 열어 주세요." },
            { "Choose Start / End, then draw a box tightly around one number at Start.", "시작·종료 프레임을 선택하고 시작 프레임에서 숫자 하나를 둘러싸는 영역을 그리세요." },
            { "Tracking finished. Review samples, correct drift, then export the preset.", "추적이 끝났습니다. 샘플을 검토하고 오차를 보정한 뒤 프리셋을 저장하세요." },
            { "Too many samples. Shorten the range or increase sample spacing (maximum 600).", "샘플이 너무 많습니다. 구간을 줄이거나 샘플 간격을 늘리세요 (최대 600개)." },
            { "Add/correct a sample at the current frame inside the range first.", "먼저 선택 구간 안의 현재 프레임에 샘플을 추가하거나 보정하세요." },
            { "Tracking position and uniform scale. Rotation and opacity remain manual. Stop is safe.", "위치와 균일 크기를 추적 중입니다. 회전·불투명도는 수동으로 보정하세요. 중단해도 결과는 유지됩니다." },
            { "VIDEO → DAMAGE PRESET", "영상 → 데미지 프리셋" },
            { "Open Studio", "스튜디오 열기" },
            { "Exit Play mode to extract video animation.", "영상 연출을 추출하려면 플레이 모드를 종료하세요." },
            { "1. Video / extraction session", "1. 영상 / 추출 세션" },
            { "Open video...", "영상 불러오기..." },
            { "Open local video (MP4 recommended)", "로컬 영상 불러오기 (MP4 권장)" },
            { "Reopen / relink", "다시 열기 / 연결" },
            { "Relink video", "영상 다시 연결" },
            { "Session", "세션" },
            { "Session loaded. Press Reopen / relink to load its video.", "세션을 불러왔습니다. 다시 열기 / 연결을 눌러 영상을 불러오세요." },
            { "Save session...", "세션 저장..." },
            { "2. Range and tracking", "2. 구간 선택과 추적" },
            { "Start frame", "시작 프레임" },
            { "End frame (disappear)", "종료 프레임 (사라짐)" },
            { "Set Start here", "현재를 시작으로" },
            { "Set End here", "현재를 종료로" },
            { "Sample every N frames", "샘플 간격 (프레임)" },
            { "Search radius (px)", "탐색 반경 (픽셀)" },
            { "Stop below confidence", "중단 신뢰도 기준" },
            { "Draw / replace number region", "숫자 영역 그리기 / 교체" },
            { "Track forward from current frame", "현재 프레임부터 추적" },
            { "Stop tracking (keep results)", "추적 중단 (결과 유지)" },
            { "Stopped. Completed samples kept.", "추적을 중단했습니다. 완료된 샘플은 유지됩니다." },
            { "4. Compare / export", "4. 비교 / 저장" },
            { "Number text", "표시할 숫자" },
            { "TMP font", "TMP 폰트" },
            { "Comparison / preset color", "비교 / 프리셋 색상" },
            { "Units per source pixel", "원본 픽셀당 단위" },
            { "Save animation preset (.asset)...", "애니메이션 프리셋 저장 (.asset)..." },
            { "Local matching estimates position + uniform scale. Rotation / opacity are manual. Camera movement is included; this is screen-space reconstruction, not recovery of the original settings. Save session to keep corrections.", "영상 비교로 위치와 균일 크기를 추정합니다. 회전·불투명도는 수동 보정합니다. 카메라 이동이 포함된 화면 기준 재현이며 원본 설정 복원은 아닙니다. 보정 내용을 유지하려면 세션을 저장하세요." },
            { "3. Correct current frame", "3. 현재 프레임 보정" },
            { "Interpolated (not yet a sample)", "보간 값 (아직 샘플이 아님)" },
            { "Manual anchor", "수동 기준점" },
            { "Center (source pixels, top-left)", "중심 (원본 픽셀, 좌상단 기준)" },
            { "Unrotated box size", "회전 전 영역 크기" },
            { "Rotation clockwise (degrees)", "시계방향 회전 (도)" },
            { "Opacity (manual)", "불투명도 (수동)" },
            { "Pin interpolated frame as manual anchor", "보간 프레임을 수동 기준점으로 고정" },
            { "Delete current sample", "현재 샘플 삭제" },
            { "Previous sample", "이전 샘플" },
            { "Next sample", "다음 샘플" },
            { "Play range", "구간 재생" },
            { "Frame", "프레임" },
            { "  (seeking...)", "  (프레임 탐색 중...)" },
            { "Original + track", "원본 + 추적 영역" },
            { "Overlay preset", "프리셋 겹쳐 보기" },
            { "Side by side", "좌우 비교" },
            { "Overlay blend", "겹쳐 보기 강도" },
            { "Manual anchor saved. Track forward, or correct additional frames.", "수동 기준점을 저장했습니다. 이어서 추적하거나 다른 프레임을 보정하세요." },
            { "Save extraction session", "추출 세션 저장" },
            { "Stores local video path, range and corrections (not the video).", "로컬 영상 경로, 구간과 보정값을 저장합니다. 영상 파일은 포함하지 않습니다." },
            { "Extraction session saved. The video remains at its original local path.", "추출 세션을 저장했습니다. 영상은 원래 로컬 경로에 유지됩니다." },
            { "Save animation preset", "애니메이션 프리셋 저장" },
            { "Creates a normal runtime preset. Keep the session separately to resume tracking.", "런타임 프리셋을 생성합니다. 추적을 이어가려면 세션을 별도로 저장하세요." },
            { "Preset saved. Open Studio and assign it to your font or prefab. Save session separately to retain corrections.", "프리셋을 저장했습니다. 스튜디오에서 폰트나 프리팹에 지정하세요. 보정 내용을 유지하려면 세션을 별도로 저장하세요." },
            { "Load a video first.", "먼저 영상을 불러오세요." },
            { "Select a valid start/end frame range.", "올바른 시작·종료 프레임 구간을 선택하세요." },
            { "Units per pixel must be positive.", "픽셀당 단위는 0보다 커야 합니다." },
            { "Add samples at both Start and End (at least two samples).", "시작·종료 프레임 모두에 샘플을 추가하세요 (최소 2개)." },
            { "Use at most 600 samples per preset; shorten the range or increase sample spacing.", "프리셋당 최대 600개의 샘플을 사용할 수 있습니다. 구간을 줄이거나 샘플 간격을 늘리세요." },
            { "Duplicate sample frames.", "중복된 샘플 프레임이 있습니다." },
            { "Samples need finite positions/angles, size >= 2 and opacity 0..1.", "샘플의 위치·각도는 유한한 값, 크기는 2 이상, 불투명도는 0~1이어야 합니다." },
            { "Choose a TMP font or import TMP Essential Resources.", "TMP 폰트를 선택하거나 TMP Essential Resources를 임포트하세요." },
            { "Video file not found. Relink the local file.", "영상 파일을 찾을 수 없습니다. 로컬 파일을 다시 연결하세요." },
            { "Unsupported video metadata. Use a constant-frame-rate MP4, up to 4096 px per side.", "지원하지 않는 영상 정보입니다. 각 변 4096픽셀 이하의 고정 프레임레이트 MP4를 사용하세요." },
            { "Video decoder is not ready or is seeking.", "영상 디코더가 준비되지 않았거나 탐색 중입니다." },
            { "No decoded frame.", "디코딩된 프레임이 없습니다." },
            { "Create a preset or select an existing .asset.\nTools > Damage Font Studio > Create Example Presets", "프리셋을 만들거나 기존 .asset을 선택하세요.\n예제 생성: Tools > Damage Font Studio > Create Example Presets" },
            { "Position: preview pixels, relative to spawn.\nScale: absolute multiplier. Rotation: Z degrees.\nColor alpha × Opacity. Same-channel segments cannot overlap.\nEdits update the preview and support Undo. Save writes the asset.", "위치: 생성 위치 기준 미리보기 픽셀 단위.\n크기: 절대 배율. 회전: Z축 각도.\n색상 알파 × 불투명도. 같은 속성의 구간은 겹칠 수 없습니다.\n수정 즉시 미리보기에 반영되며 실행 취소를 지원합니다. 저장하면 에셋에 기록됩니다." },
            { "Preview uses a copy. Root motion applies to the whole hierarchy.\nOriginal scale, rotation and child layout are preserved. Color is a tint multiplier.\nTMP / UI Graphic / SpriteRenderer support color and opacity. Scripts and Animator are disabled in preview.", "복사본으로 미리 봅니다. 루트의 움직임은 모든 자식에 적용됩니다.\n원래 크기·회전·자식 배치를 유지하며 색상은 곱셈 방식입니다.\nTMP / UI Graphic / SpriteRenderer의 색상·불투명도를 지원합니다. 미리보기 중 스크립트와 Animator는 비활성화됩니다." },
            { "Drag to select a number. Coordinates use original video pixels. Cyan = manual; yellow = automatic/interpolated.\nComparison fits the selected font to the first box; exported scale remains relative to your font/prefab.", "드래그하여 숫자 영역을 선택하세요. 좌표는 원본 영상 픽셀 기준입니다. 청록색 = 수동, 노란색 = 자동/보간.\n비교 폰트는 첫 영역에 맞춰집니다. 저장되는 크기는 폰트/프리팹 기준의 상대 배율입니다." },
            { "{0}  |  {1} instances  |  {2:0.00} / {3:0.00} s", "{0}  |  {1}개  |  {2:0.00} / {3:0.00} 초" },
            { "{0} × {1}  |  {2:0.###} fps  |  {3} frames", "{0} × {1}  |  {2:0.###} fps  |  {3} 프레임" },
            { "Auto · confidence {0:0.00}", "자동 · 신뢰도 {0:0.00}" },
            { "{0} samples · {1} low confidence", "샘플 {0}개 · 낮은 신뢰도 {1}개" },
            { "Decoded frame {0}  ·  {1:0.000}s", "디코딩된 프레임 {0}  ·  {1:0.000}초" },
            { "Low confidence ({0:0.00}) at frame {1}. Correct the box and Continue tracking.", "프레임 {1}의 신뢰도가 낮습니다 ({0:0.00}). 영역을 보정하고 추적을 이어가세요." },
        };
    }
}
