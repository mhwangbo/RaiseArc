---
id: installation
title: 설치
---

Unity Hub에서 Unity 6000.6.0f1과 Windows Build Support를 설치하고 새 2D 프로젝트를 만드세요. 아래 비공개 Git 후보는 새 소비자 프로젝트에서 의존성 해결과 컴파일을 확인했습니다. 전체 제작·실행 절차는 계속 검증 중입니다.

1. Unity에서 **Window → Package Management → Package Manager**를 엽니다.
2. **+ → Install package from git URL**을 선택합니다.
3. `https://github.com/mhwangbo/RaiseArc.git?path=/Packages/io.github.mhwangbo.raisearc#e471cfb1134fa8f2b513ca31a74a39bb0f86cea7`을 입력합니다. 현재 비공개 저장소 접근 권한이 필요합니다. 더 새 커밋은 설치 검증 후 사용합니다.
4. RaiseArc 패키지가 보이고 Console에 빨간 컴파일 오류가 없는지 확인합니다.
5. **Window → RaiseArc** 메뉴를 확인합니다. 기본 제작에는 MCP가 필요하지 않습니다.

로컬 개발자는 저장소의 `DevProject`를 열면 됩니다. 해당 프로젝트는 형제 `Packages` 폴더의 소스 하나만 참조합니다.

패키지를 찾지 못하면 저장소 권한과 커밋을 확인하세요. 타입 중복 오류가 나면 예전 `Assets/PrincessStudio`가 남아 있는지 확인하고 [이전 안내](migration.md)를 따르세요.
