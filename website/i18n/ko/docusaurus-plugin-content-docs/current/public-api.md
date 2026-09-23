---
id: public-api
title: Runtime과 Authoring API
---

일반 제작자는 Studio와 UI Toolkit 바인딩을 사용합니다. 개발자는 게임당 하나의 `RaiseArc.Unity.RaiseArcSessionHost`를 만들고 여러 화면에 연결할 수 있습니다. 화면이 재생성돼도 호스트를 유지하고, 화면 활성화 때 구독하며 제거할 때 해제하세요. Load·Restart 뒤에는 호스트에서 선택지와 상태를 새로 읽습니다.

```csharp
var host = new RaiseArc.Unity.RaiseArcSessionHost(projectAsset, saveDirectory, seed: 123);
host.Changed += RefreshScreen;
host.StartActivity(activityId);
```

호스트가 계획을 소유할 때 상태 변경은 호스트 메서드를 사용합니다. Editor 통합은 현재 revision을 가진 `AuthoringService`의 검증·커밋 경로를 사용합니다. 별도 Editor 호스트는 Undo, dirty 표시, 저장을 책임집니다.

확장은 `ICondition`, `IEffect`, `IGameModeModule`, `ISaveParticipant` 등을 명시적으로 등록합니다. 설치된 확장 코드는 신뢰된 응용 코드이지 보안 샌드박스가 아닙니다. 저장은 버전 있는 JSON, 체크섬, 임시 파일, 백업을 사용합니다. 체크섬은 손상 감지용이며 암호화가 아닙니다. 프로젝트·콘텐츠 불일치는 명시적인 이전이 필요합니다.
