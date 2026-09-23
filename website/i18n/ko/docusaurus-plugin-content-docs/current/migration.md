---
id: migration
title: 이전과 업그레이드
---

**이전 검증은 아직 완료되지 않았습니다. 원본 프로젝트와 세이브를 보존하세요.** 복사본에서만 테스트합니다.

새 패키지는 직렬화 호환성을 위해 `PrincessStudio.*` assembly·namespace·타입, GUID, 세이브 ID, Localization 식별자, `princess-studio-` MCP pipe 이름을 남깁니다. 사용자에게 보이는 새 이름은 RaiseArc를 씁니다.

`Assets/PrincessStudio`와 UPM 패키지를 한 프로젝트에 동시에 설치하면 중복 스크립트가 생깁니다. 먼저 옛 폴더의 사용자 수정 파일과 `.meta`를 조사하고 별도로 백업하세요. 검증된 옛 패키지 파일만 복사 프로젝트에서 제거한 뒤 새 패키지를 설치합니다. Scene·Prefab의 Missing Script를 확인합니다.

옛 세이브의 대화 대기, 초안·확정 계획, 기록, 난수를 검사하세요. 실패 시 원본 세이브를 유지해야 합니다. 게임 제목과 회사명은 사용자 게임의 정체성이므로 RaiseArc로 강제 변경하지 않습니다.
