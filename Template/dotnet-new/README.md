# SuperSocketLite2.Templates

`dotnet new` 템플릿 패키지. 실행 가능한 SuperSocketLite2 서버 하나와,
**AI 코딩 에이전트용 가이드를 함께** 스캐폴딩한다.

## 왜 있나

NuGet 패키지만 참조하는 프로젝트에는 DLL과 `PackageReadme.md` 밖에 가지 않는다.
그 프로젝트에서 도는 에이전트는 `ReceiveFilter` 를 어떻게 짜는지도, 이 라이브러리의
zero-copy 계약도 모른다. NuGet으로는 이 문제를 풀 수 없지만 **템플릿으로는 풀린다** —
생성된 프로젝트가 `AGENTS.md` · `CLAUDE.md` · `.claude/skills/supersocketlite2/` ·
`Docs/agent/` 를 처음부터 들고 태어난다.

## 사용

```bash
dotnet new install SuperSocketLite2.Templates
dotnet new sslite2-server -n MyGameServer
cd MyGameServer
dotnet run -c Release
```

### 옵션

| 옵션 | 기본값 | 설명 |
|---|---|---|
| `--Port` | 32452 | listen 할 TCP 포트 |
| `--MaxConnection` | 2000 | 최대 동시 접속 수 |
| `--agentGuidance` | true | 에이전트 가이드를 함께 생성한다. 끄지 않기를 권한다 |
| `--skipRestore` | false | 생성 후 `dotnet restore` 를 실행하지 않는다 |

## 개발

에이전트 가이드는 템플릿 폴더에 복사본을 두지 않는다. `SuperSocketLite2.Templates.csproj` 가
**pack 할 때 저장소 원본을 직접 집어넣는다.**

| 원본 | 생성된 프로젝트 안 위치 |
|---|---|
| `Docs/agent/*.md` | `Docs/agent/` |
| `.claude/skills/supersocketlite2/**` | `.claude/skills/supersocketlite2/` |

**원본을 고치면 그것으로 끝이다.** 동기화할 것이 없으니 어긋날 일도 없다.
원본이 사라진 채로 배포되는 것을 막으려고 `VerifyAgentGuidance` 타깃이 pack 전에 존재를 확인한다.

### 로컬 테스트

```bash
dotnet pack Template/dotnet-new -c Release -o ./artifacts

dotnet new install ./artifacts/SuperSocketLite2.Templates.0.21.1.nupkg
dotnet new sslite2-server -n TestServer -o /tmp/TestServer
dotnet build /tmp/TestServer -c Release

dotnet new uninstall SuperSocketLite2.Templates
```

### 배포

```bash
dotnet nuget push ./artifacts/SuperSocketLite2.Templates.0.21.1.nupkg \
  --source https://api.nuget.org/v3/index.json --api-key <KEY>
```

버전은 라이브러리 패키지(`SuperSocketLite2`)와 맞춰 둔다. 라이브러리를 올리면
`SuperSocketLite2.Templates.csproj` 의 `PackageVersion` 과, 생성될 프로젝트의
`PackageReference` 버전(`content/SuperSocketLite2.GameServerTemplate/*.csproj`)을 같이 올린다.
