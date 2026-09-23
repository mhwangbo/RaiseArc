---
id: analyze-ending
title: 엔딩 경로와 경제 변경 검사
---

**시작 상태:** 활동 둘 이상과 능력치 조건 엔딩이 있는 저장된 게임 에셋. Explorer는 저장된 별도 스냅샷을 분석하므로 편집 내용을 먼저 저장합니다.

1. Studio에서 엔딩을 선택해 **Find a path to the saved ending**을 엽니다. 목표 엔딩과 필요하면 활동 제한을 정합니다.
2. **Find path / rerun test**를 실행합니다. 목표 통과 여부와 중단 이유를 따로 읽고 실제 돈·능력치 변화가 있는 경로를 확인합니다. **Verify replay**로 입력을 다시 실행합니다.
3. **Save test definition**과 **Preserve result and original content**를 실행하고 결과 ID를 적습니다. 보존 결과는 원래 콘텐츠와 런타임 fingerprint에 속합니다.
4. Studio에서 한 활동 비용을 올려 저장합니다. 테스트를 다시 실행해 기존 경로의 차이를 비교하고, 예산과 제한 안에서 대체 경로를 찾습니다.
5. 비교 결과를 내보낸 뒤 다시 열어 결과 ID, 콘텐츠 식별, 실제 반영 변화량, 목표 판정, 재현 상태를 확인합니다. 옛 런타임이 없으면 정확 재현은 미지원으로 표시돼야 합니다.

결과는 Unity 프로젝트 루트의 `RaiseArcAnalysisResults`에 있습니다. 플레이어 세이브가 아니므로 보존하려면 별도로 백업하세요. 새 패키지에서 내보내기·재열기 전체 절차 검증은 남았습니다.
