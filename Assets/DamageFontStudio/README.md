# Damage Font Studio — 에디터 제작 환경

Unity 6000.3.24f1 / TextMeshPro / DOTween 무료 버전 기반. ProjectAG 참조는 없습니다.

**게임 적용 규칙:** 게임에서 이미 사용하는 데미지 폰트 프리팹에 추출한 연출만 적용합니다. 게임에 연결할 최종 파일은 **`GameMotion.asset` 하나**입니다. 더블클릭하거나 Inspector의 **이 파일을 재생 테스트에서 확인**을 누르고 **기존 게임 데미지 프리팹 / 오브젝트**를 지정하면, 게임에 연결할 바로 그 프리셋으로 숫자 변경·단발·연타·반복을 확인할 수 있습니다. 폰트·머티리얼·색상·기본 크기를 유지하고 상대 이동·상대 크기 변화·회전·투명도만 적용합니다.

`Light-01`, `Light-02` 등은 각각 추적한 타격이며 버전 번호가 아닙니다. 타격별 후보와 샘플은 **Analysis** 아래에 보존하고 `RecordedHits`는 타격별 연출·간격을 비교하는 분석 자료로 유지합니다. **타격별 분석 결과 / 최종본 변경**에서 다른 타격을 선택하면 기존 `GameMotion.asset`의 내용만 바뀌므로 게임의 에셋 연결은 유지됩니다. 평균 연출로 합치지 않습니다. 기존 분석 폴더의 파일은 이동하거나 삭제하지 않습니다.

프리팹 없이 확인하려면 재생 테스트의 **기본 숫자로 재생 (프리팹 불필요)**를 누르세요. 기본 TMP 숫자를 크기 64·흰색으로 표시하고, 미리보기 생성 위치·확대·무작위 분산을 초기화하여 반복 재생합니다. 숫자가 비어 있으면 `123456`을 사용합니다. 최종 연출 파일은 수정하지 않습니다. 게임 프리팹이 없는 `GameMotion.asset`을 더블클릭해도 이 경로로 열립니다. 재생을 만들지 못하면 오류를 창에 표시합니다.

## 시작

**RecordedHits 전체 결과 재생**: 생성된 `RecordedHits.asset`을 선택하고 Inspector의 **재생 테스트에서 전체 재생**을 누릅니다. Studio에서도 **추출된 연타 시퀀스**에 에셋을 넣고 **전체 시퀀스 재생**을 누를 수 있습니다. 개별 샘플을 반복하는 대신 저장된 타격 간격과 연출 전체를 재생하며 숫자 변경·반복·일시정지·배속·시간 이동을 지원합니다. Play 모드나 원본 영상은 필요 없습니다.

### 에디터 언어

Studio 및 영상 추출 창 상단의 **언어 / Language**에서 **한국어 / English**를 선택합니다. 기본값은 한국어입니다. 선택은 이 컴퓨터의 Unity EditorPrefs에 저장되어 에디터를 다시 열어도 유지되며, 두 창과 프리셋·Damage Font Object Inspector에 공통 적용됩니다. Inspector에서도 언어를 바꿀 수 있습니다.

버튼, 설정 항목, 데미지 종류·속성·Ease 선택지, 안내와 검증 메시지를 번역합니다. 언어 변경은 프리셋 값이나 재생 결과를 바꾸지 않습니다. Unity 자체 메뉴·기본 에셋 선택창·외부 오류 메시지와 사용자가 지정한 파일명은 원래 표시를 유지합니다. 아래 사용 설명의 영문 명칭은 English 선택 시 표시되는 명칭입니다. 미리보기에서 출력하는 텍스트의 한글 지원은 별도로 한글을 포함한 TMP Font Asset이 필요합니다.

영상 기반 추출: **Tools > Damage Font Studio > Extract From Video** 또는 Studio 상단 **Extract from video**. **영상 불러오기 → 미리보기에서 시작·종료 지정 → 분석 시작 → 샘플 자동 생성** 순서로 진행합니다. 영상을 불러올 때는 분석하지 않습니다. 프리셋·프리팹과 타격별 시각을 담은 **RecordedHits**를 생성하고 원본과 나란히 비교합니다. 숫자는 자유롭게 바꿀 수 있으며 폰트 외형과 페이드는 근사치입니다. 기존 수동 영역 추적과 보정도 지원합니다. 상세 절차와 한계는 `Editor/Extraction/README.md`를 참고하세요.

1. 이 프로젝트에서 컴파일이 끝나면 **Tools > Damage Font Studio > Open Studio**를 엽니다. Play 모드는 필요 없습니다.
2. 기본 Normal 프리셋이 선택됩니다. 다른 프리셋은 `Presets`의 Normal, Critical, Incoming, Skill `.asset`을 Preset 필드로 드래그합니다.
3. **Single**, **Hit series**, **Burst × 50**으로 재생합니다. Repeat, Pause/Resume, Speed, Time 슬라이더를 지원합니다.
4. Light background로 배경을 전환합니다. Preview zoom은 설정 데이터에 영향을 주지 않습니다.
5. 왼쪽 설정을 수정하면 현재 시간에서 미리보기가 갱신되고 일시정지합니다. 다시 재생하려면 Resume 또는 Single을 누릅니다.
6. **Save**로 `.asset`을 저장합니다. **New / Duplicate**로 새 프리셋을 만듭니다. 변경은 Unity Undo를 지원합니다. Unity의 일반 프로젝트 저장으로도 저장됩니다.

재생 모드를 바꾸면 처음부터 재생합니다. **시간** 슬라이더를 옮기면 해당 시점에서 일시정지하며 **계속 재생**으로 이어갑니다. 끝에서 계속 재생하면 처음부터 시작합니다. **반복**은 마지막 연타까지 끝난 전체 구간을 반복합니다. 재생 중 숫자·연타 횟수·간격·분산·프리셋을 수정하면 현재 모드를 유지해 미리보기를 갱신하고 일시정지합니다. **정지 / 초기화**는 미리보기와 시간을 정리하며 입력한 테스트 설정은 유지합니다.

재생 조합 검사: **Tools > Damage Font Studio > Run Studio Playback Checks**. 기본 및 추출 프리셋의 모드·반복·배속 조합, 일시정지·시간 이동·종료·재시작, 설정 변경과 미리보기 정리를 검사하며 결과는 `Logs/StudioPlaybackChecks.txt`에 저장합니다. 검사는 별도 미리보기 창과 프리셋 복사본에서 진행합니다.

## 여러 프리셋을 한 화면에서 비교

재생 테스트 상단에서 **여러 프리셋 비교** 탭을 선택합니다. 왼쪽 **비교 프리셋** 목록의 **프리셋 슬롯 추가**로 `.asset`을 지정하거나 **현재 프리셋 추가**를 누릅니다. 여러 프리셋 에셋을 목록에 한꺼번에 드래그해도 됩니다. 각 행의 **편집**은 왼쪽 편집 대상을 선택하고, 화살표는 순서 변경, **×**는 목록에서 제거합니다. 원본 에셋은 삭제하지 않습니다.

등록한 수만큼 미리보기 영역을 나눕니다. **분할 열 수**의 기본값은 자동이며 1~6열을 직접 지정할 수 있습니다. 각 영역에 이름과 시간이 표시되고 별도 미리보기 씬으로 렌더링하므로 다른 프리셋의 연출과 겹치지 않습니다. 숫자가 작거나 연출이 잘리면 창 크기, **미리보기 확대**, **비교 영역 크기**를 조절하세요. 비교 영역 크기는 카메라가 보는 가상 공간이며 프리셋 값에 영향을 주지 않습니다.

**단발 / 연타 / 50개 동시 출력**은 각 등록 프리셋에 동일하게 적용됩니다. 숫자·프리팹·생성 위치·랜덤 시드·연타 간격도 공통으로 사용합니다. 반복·배속·일시정지·시간 이동은 모든 영역을 함께 제어합니다. 짧은 연출은 먼저 종료되고 가장 긴 연출이 끝나면 전체 반복을 시작합니다. 비교 중 목록이나 연출 설정을 수정하면 현재 시점에서 갱신 후 일시정지합니다. 정지하거나 다른 탭으로 이동해도 등록 목록은 유지됩니다. 빈 슬롯이나 오류가 있는 프리셋은 해당 영역에 안내하고 유효한 프리셋은 계속 비교할 수 있습니다.

검사 메뉴: **Tools > Damage Font Studio > Run Comparison Checks**. 결과는 `Logs/StudioComparisonChecks.txt`, 비교 렌더링은 `Logs/ComparisonRender.png`에 저장합니다.

## 구간 설정

`Segments`를 펼쳐 구간을 추가합니다. Start/Duration은 초 단위입니다.

- Position: 생성 위치를 기준으로 한 이동량. 에디터 화면에서 zoom 1일 때 픽셀 단위입니다.
- Scale: 초기 크기에 대한 상대 증분이 아닌 최종 배율입니다.
- Rotation: Target Value는 Z축 각도입니다.
- Opacity: Target Value는 0~1이며 최종 알파는 Color.a와 곱해집니다.
- Color: Target Color를 사용합니다.
- Ease 또는 Use Curve를 활성화한 Curve로 보간합니다. 커브는 시간/진행률 0~1 기준입니다.
- 각 구간 시작값은 같은 채널의 이전 구간 목표값입니다. 구간 사이 빈 시간에는 이전 값을 유지합니다.
- 동일 채널의 겹친 구간은 오류로 표시하며 재생하지 않습니다. 다른 채널은 동시에 재생됩니다.
- 연출 길이는 모든 구간의 마지막 종료 시간입니다. 종료 시 표시가 숨겨집니다.

숫자, 연타 수/간격, 생성 위치, 분산, 시드는 테스트 조건이며 프리셋에 저장되지 않습니다. 기본적으로 모든 숫자는 같은 위치에서 생성됩니다. **생성 위치 무작위 분산**을 직접 켠 경우에만 분산·시드를 사용하며, 프리팹의 미리보기 버튼으로 열면 분산을 끕니다. 고정 시드는 분산을 사용한 시간 이동·반복 재생에서 같은 배치를 보장합니다. 50개 동시 출력은 해당 재생에만 간격 0을 적용하며, 연타 간격 설정을 유지합니다. 이후 연타를 누르면 설정한 간격으로 순서대로 출력합니다. 기본 폰트는 숫자/영문용이며 한글 표시는 한글을 포함한 TMP Font Asset을 지정하세요. Damage Kind는 프리셋 분류입니다. 종류별 연출은 해당 프리셋을 선택하여 편집합니다.

## 프리팹 / 씬 오브젝트 연결

Studio의 **Prefab / Scene Object** 필드에 Project 창의 프리팹 또는 Hierarchy의 오브젝트를 드래그하고 **Single / Hit series**로 재생합니다. 비워두면 기존처럼 기본 TMP 텍스트를 생성합니다. 원본은 건드리지 않고 Preview Scene의 복제본에만 연출을 적용합니다.

- 루트 Transform에 이동·포물선·크기·Z 회전을 적용합니다. 자식은 함께 움직이며 자식 Transform을 직접 바꾸지 않습니다.
- 프리팹 모드의 크기는 원래 루트 크기 × 설정 배율, 회전은 원래 루트 회전 + 설정 Z 회전입니다.
- 자식 TMP 텍스트, UI Graphic(Image/RawImage 등), SpriteRenderer의 원래 색에 프리셋 색상을 곱합니다. 원래 색을 유지하려면 Initial Color를 흰색으로 두세요. MeshRenderer의 셰이더별 색상/투명도와 파티클 애니메이션은 제어하지 않습니다.
- 기본적으로 프리팹의 폰트·폰트 크기를 유지합니다. **Override text style**을 켜면 숫자 텍스트의 폰트·크기와 자식 TMP들의 색상을 프리셋 값으로 지정합니다.
- 숫자는 기본적으로 첫 TMP 텍스트에 표시합니다. 특정 자식을 지정하려면 아래 컴포넌트의 **Number Text**를 연결하세요. TMP가 없는 오브젝트도 Transform 연출을 적용할 수 있습니다.
- UI는 미리보기 전용 World Space Canvas에서 표시합니다. Preview zoom 1에서는 로컬 1단위가 화면 약 1픽셀입니다. 작은 월드 오브젝트는 zoom을 키우고 이동량·포물선 높이도 월드 크기에 맞춰 설정하세요.
- 미리보기 복제본에서는 일반 스크립트, Animator, 파티클 재생을 끕니다. 복잡한 스크립트 기반 UI는 필요한 외형이 프리팹에 저장되어 있어야 합니다.

연결을 오브젝트/프리팹에 저장하려면 루트에 **Add Component → Damage Font Studio → Damage Font Object**를 추가합니다. **Preset**에 연출 `.asset`, **Number Text**에 대상 TMP 자식을 지정한 뒤 **Preview in Damage Font Studio**를 누릅니다. 프리팹 인스턴스의 연결 정보를 원본에도 저장하려면 Unity의 일반 Apply 절차를 사용하세요. 씬 오브젝트 참조는 연출 `.asset`에 저장하지 않습니다.

이 컴포넌트는 Play 모드에서도 Inspector의 **Play / Cancel** 또는 `Play("123", completed)`로 같은 재생 코드를 실행합니다. **Play On Enable**은 활성화될 때 재생하는 옵션입니다. 완료하면 루트를 비활성화하며, 비활성화·파괴·취소 시 트윈을 정리합니다. 다시 사용할 때는 생성 위치를 지정한 후 활성화하거나 Play를 호출합니다. 숫자 대상과 스타일 옵션은 재생 객체 생성 전 설정하세요. 동일 루트의 Transform/색상을 Animator 등으로 동시에 제어하면 충돌할 수 있습니다.

## 포물선으로 생성 후 낙하

`FallingArc.asset` 프리셋을 선택하고 Single 또는 Hit series로 확인합니다. 기존 프리셋에서는 Position 구간의 **Position Path → Parabola**를 선택합니다.

- **Target Vector**: 최종 이동량. 예: `(160, -120, 0)`은 오른쪽 160, 아래 120으로 낙하합니다. X를 음수로 설정하면 왼쪽으로 낙하합니다.
- **Arc Height**: 시작점과 끝점을 잇는 직선보다 경로 중간에서 얼마나 위로 솟는지 설정합니다. 예: `140`. 시작 높이 기준의 절대 최고점은 아닙니다.
- **Duration**: 전체 비행 시간. 예: `1.1`초.
- **Ease = Linear**: 일정한 가로 속도와 중력처럼 가속되는 낙하를 만듭니다. Ease/Curve를 바꾸면 같은 경로를 따라가는 속도가 바뀝니다. Back/Elastic 또는 0~1을 벗어나는 커브는 경로의 시작/끝을 넘어갈 수 있습니다.
- 처음 위로 상승하려면 `4 × Arc Height + (끝 Y - 시작 Y)`가 양수여야 합니다. 낙하 거리가 너무 크면 높이를 올리세요.

이동 궤적은 DOTween Sequence의 같은 시간 축에서 계산하므로 일시정지, 시간 이동, 연타, 반복 및 `.asset` 저장을 그대로 지원합니다. Direct는 기존 이동 방식입니다.

## 구조와 수명 관리

- `Runtime/DamageFontPreset.cs`: ScriptableObject와 구간 정의.
- `Runtime/DamageFontPlayback.cs`: TMP 또는 GameObject 대상에 DOTween Sequence를 생성. `Prepare`, `Play`, `Pause`, `Seek`, `Cancel`, `Dispose` 제공.
- `Runtime/DamageFontTarget.cs`: 루트 Transform 및 자식 TMP/UI/Sprite 표시 상태 적용·복원.
- `Runtime/DamageFontObject.cs`: 프리팹/씬 오브젝트의 연출·숫자 대상 연결과 재생 수명 관리.
- `Editor/DamageFontStudioWindow.cs`: Preview Scene 기반 독립 미리보기. 현재 씬을 수정하지 않습니다.
- `Editor/StudioVerification.cs`: **Run Playback Checks** 메뉴에서 왕복 시간 이동, 반복 재사용, 취소, 저장/로드, 트윈 정리를 검사합니다. 결과는 `Logs/FontStudioVerification.txt`에 기록됩니다.

런타임 사용 시 대상 TMP_Text와 폰트를 준비하고 `Prepare(..., completed)` 후 `Play()`합니다. 완료 콜백에서 풀 반환을 처리하고, 풀 반환/비활성화/파괴 시 반드시 `Cancel` 또는 `Dispose`를 호출합니다. 재사용 시 `Prepare`가 기존 트윈을 먼저 제거합니다. 에디터는 정지된 개별 Sequence의 Goto를 사용하며 전역 DOTween 시간을 움직이거나 KillAll을 호출하지 않습니다.

## 현재 범위

이번 단계는 에디터 설정·미리보기 환경까지입니다. `.unitypackage` 생성, 타 프로젝트 임포트 검증, ProjectAG 인게임 연결은 포함하지 않습니다.

DOTween 1.3.030은 공식 배포본을 `Assets/Plugins/Demigiant/DOTween`에 설치했습니다. Pro 기능은 사용하지 않습니다. 향후 툴 패키지에는 이 폴더를 포함하지 않고 대상 프로젝트의 DOTween 설치를 의존성으로 안내해야 합니다. TMP Essential Resources는 Unity uGUI 패키지에 포함된 공식 리소스입니다.

DOTween 출처 및 라이선스: https://dotween.demigiant.com/download.php / https://dotween.demigiant.com/license.php

## 검증 기록 (2026-09-18)

지정 프로젝트 내부 `Temp/FontStudioValidation` 검증 복사본에서 Unity 6000.3.24f1 배치 실행을 완료했습니다. 현재 프로젝트의 URP 패키지와 Graphics/Quality 설정을 적용한 검사도 종료 코드 0으로 통과했습니다. 별도 `.unitypackage` 임포트 검증은 아닙니다.

검사 내용: 이동/크기/회전/색상/투명도/커브, 역방향 시간 이동, 아직 시작하지 않은 연타 숨김, 100회 재사용, 완료/취소 콜백, 완료 콜백 내 풀 반환, `.asset` 저장 후 재생 일치, 잘못된 구간 중첩 거부, 트윈 잔여 0개, 밝은/어두운 배경 렌더 및 미리보기 정리. `Logs/FontStudioVerification.txt`, `Logs/FontStudioBatch-URP.log`, `Logs/FontStudioPreview-*.png`에 결과가 있습니다. 실제 에디터 창의 마우스 조작에 대한 수동 검증은 별도입니다.

오브젝트 연결 확장 검사: UI 이미지 + TMP 자식이 있는 씬 오브젝트와 저장된 프리팹의 렌더, 원본 보존, 루트 크기·회전·자식 배치 보존, Stretch 앵커의 미리보기 크기, 텍스트가 없는 SpriteRenderer의 투명도, 20회 재사용 시 알파 누적 방지를 확인했습니다. 검증 복사본의 실제 Play 모드에서도 Play On Enable, 이동, 재재생 시 위치 누적 방지, 완료 콜백 1회, 비활성화 정리, 풀 재사용 위치, 취소를 검사했습니다. `Logs/FontStudioBatch-Objects.log`, `Logs/FontStudioObjectPlayMode.txt`를 참고하세요.

