# SuperSocketLite2
  
SuperSocketLite의 업그레드 버전이다.  
.NET 플랫폼을 지원한다.
SuperSocketLite2은 게임 서버 개발에 사용하는 것을 주 용도로 예상하고 있지만, 일반적인 Socket 서버 개발에도 사용할 수 있다.      
  
SuperSocketLite2는 고성능, 안정성, 사용 편이를 목표로 한다.

## VS Code에서 저장소 전체 분석

저장소 루트의 `SuperSocketLite2.slnx`에는 라이브러리, 템플릿, 테스트, 튜토리얼을 포함한
C# 프로젝트 33개가 등록되어 있다. VS Code에서 저장소 루트를 열었을 때 C# 확장이 이 통합
솔루션을 자동으로 선택하지 않으면, 열어 본 파일만 임시 `Canonical.csproj`로 분석한다.
이 상태에서는 같은 파일이나 .NET 기본 라이브러리의 정의는 F12로 이동되지만, 아직 로드되지
않은 다른 소스 파일의 정의는 이동되지 않을 수 있다.

저장소 전체를 분석하려면 `.vscode/settings.json`에 다음 작업 영역 설정을 둔다.

```json
{
  "dotnet.defaultSolution": "SuperSocketLite2.slnx"
}
```

설정 후 명령 팔레트에서 `개발자: 창 다시 로드`를 실행한다. `출력` 패널의 `C#` 로그에서
임시 `Canonical.csproj`가 아니라 `SuperSocketLite2.slnx`와 그 프로젝트들이 로드되는지
확인한다. `.vscode/`는 이 저장소의 `.gitignore` 대상이므로 이 설정은 각 개발 환경에서
로컬로 생성해야 한다.

자세한 원인과 확인 절차는 [VS Code 저장소 전체 분석 매뉴얼](Docs/VSCode_Repository_Analysis.html)에 있다.
