---
id: first-game
title: 첫 육성게임 만들기
---

**시작 상태:** [설치](installation.md)를 마친 새 Unity 6000.6.0f1 프로젝트. 이 절차는 이전 Assets 설치 후보에서 작성됐으며 새 UPM 후보에서 끝까지 다시 검증해야 합니다.

1. **Window → RaiseArc → Make a game**에서 게임 제목과 캐릭터 이름을 적습니다. 나이 **10**, 기간 **42**, 돈 **60**, 초기 능력치 세 개 **10**을 사용하고 **Choose one activity at a time**, **Include a minimal starting game**을 켠 뒤 **Create my game**을 누릅니다. 새 게임 에셋 경로가 표시됩니다.
2. **Activities**에서 Study의 이름과 설명을 수정해 저장합니다. 고유 ID를 가진 활동 하나를 추가하고 비용과 능력치 효과를 정합니다. 목록에 두 활동이 나타나야 합니다.
3. **Events**에서 초대 사건에 두 대사와 두 선택지를 넣습니다. **Endings**에서 능력치 조건이 있는 엔딩과 대체 엔딩을 수정합니다. 검증 화면에 깨진 참조가 없어야 합니다.
4. **Window → RaiseArc → Samples → Create native UI example**로 화면을 생성합니다. 게임 폴더의 `NativeGame.unity`를 열고 Play를 누릅니다. 활동 카드가 보여야 합니다. 템플릿 누락 오류는 후보의 실패로 기록합니다.
5. 계획에 활동을 넣어 확정하고 한 번 실행합니다. 돈과 능력치가 한 번만 바뀌는지 확인합니다. 대사를 진행해 선택하고 명시적으로 재개합니다.
6. 선택 대기 중 저장하고 Play를 종료한 뒤 다시 시작해 불러옵니다. 같은 선택과 계획이 복원되고 활동이 자동 실행되지 않아야 합니다. 엔딩까지 진행합니다.
7. **File → Build Profiles → Scene List**에 장면을 넣고 Windows 빌드를 새 폴더에 만듭니다. 실행 파일을 열어 활동·대화·저장·재시작·복원·엔딩을 확인합니다.

결과 게임 에셋과 UI 파일은 프로젝트의 `Assets/RaiseArcGames`에 남습니다. 빌드 성공만으로 실행 동작을 확인한 것은 아닙니다.
