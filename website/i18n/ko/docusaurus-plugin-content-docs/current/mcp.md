---
id: mcp
title: 선택적인 MCP 연결
---

기본 게임 제작과 플레이에는 MCP·Python·LLM이 필요하지 않습니다. 현재 선택적 adapter는 Windows Editor와 Python 3.10 이상을 사용하며 Python 표준 라이브러리만 필요합니다.

1. Studio에서 대상 게임을 선택하고 **LLM commands → Enable local MCP**를 켜서 표시된 pipe 이름을 복사합니다. 이전 설정 호환성을 위해 `princess-studio-` 접두사를 유지합니다.
2. 설치된 패키지의 `Integrations/MCP/raisearc_mcp.py` 경로를 확인합니다. MCP 클라이언트의 stdio 명령을 `python <설치된-스크립트-절대경로> --pipe <Unity에-표시된-이름>`으로 설정합니다. 개인 컴퓨터의 경로를 공유 설정에 올리지 마세요.
3. `tools/list`로 실제 스키마를 읽고 `ReadProject` 또는 `GetContentIndex`로 프로젝트와 revision을 확인합니다. `CreateActivity` 같은 변경에는 해당 `expectedRevision`을 사용합니다. Graph 편집은 `PreviewChangeSet`과 `CommitChangeSet` 경로를 따릅니다.
4. 끝나면 Studio에서 MCP를 끕니다. 프로젝트 전환 후에는 새 대상과 pipe를 확인합니다.

요청 크기 제한, 허용된 명령 목록, Windows 현재 사용자용 named pipe ACL이 소스에 있습니다. 실제 설치된 클라이언트와 프로젝트 전환의 전송 검사는 공개 전 과제입니다. 설치된 버전의 `tools/list`가 정확한 필드와 제한의 기준입니다.
