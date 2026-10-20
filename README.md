# Damage Font Studio

Unity 에디터에서 데미지 숫자 연출을 제작·비교하고 영상에서 이동·크기·회전·투명도 변화를 추출하는 독립 프로젝트입니다.

- Unity **6000.3.24f1**로 프로젝트 루트를 엽니다. 패키지 복원이 끝나고 컴파일이 완료될 때까지 기다립니다.
- **Tools > Damage Font Studio > Open Studio**: 프리셋 제작과 단발·연타·동시 출력 미리보기, 여러 프리셋 분할 비교.
- **Tools > Damage Font Studio > Extract From Video**: 로컬 영상 분석, 추적 보정, 프리셋·샘플 생성.
- 에디터 UI는 한국어·영어 전환을 지원합니다. Play 모드는 필요 없습니다.

자세한 사용법은 [Studio 사용 안내](Assets/DamageFontStudio/README.md)와 [영상 추출 안내](Assets/DamageFontStudio/Editor/Extraction/README.md)를 참고하세요.

## 저장소 구성

`Assets`, `Packages`, `ProjectSettings`와 문서를 보존합니다. 기본 프리셋, 직접 제작한 프리셋 및 `Assets/DamageFontStudio/Generated`의 추출 결과도 포함됩니다. 원본 영상은 저장소에 포함하지 않으며 추출 세션을 다시 사용할 때 로컬 영상 경로를 연결해야 할 수 있습니다.

DOTween 무료 버전과 TMP 리소스, 영상 분석용 ONNX 모델 및 해당 라이선스·출처를 함께 보존했습니다. 모델은 에디터 분석에 필요합니다. 향후 다른 프로젝트로 툴을 패키징할 때의 DOTween 의존성 안내는 Studio 사용 안내를 참고하세요.

Unity 캐시(`Library`, `Temp`), 로그, IDE 생성 파일, 로컬 설정, 임시 검증 프로젝트와 렌더링 결과는 Git에서 제외합니다. 새로 받은 프로젝트에서는 Unity가 필요한 캐시와 솔루션 파일을 다시 생성합니다.

## 검증

Studio의 **Run Studio Playback Checks**, **Run Comparison Checks**와 기타 검증 메뉴를 사용할 수 있습니다. 검사 결과는 Git에서 제외되는 `Logs`에 생성됩니다. 실제 영상 기반 검증 중 일부는 별도의 로컬 원본 영상이 필요합니다.
