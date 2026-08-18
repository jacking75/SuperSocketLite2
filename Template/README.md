# 게임 서버 템플릿
이 디렉토리에 있는 게임 서버 프로젝트를 사용하여 빠르게 게임 서버를 개발하도록 한다. 

서버 프로젝트 3개는 이 저장소의 라이브러리 소스를 직접 참조하지 않고 nuget.org의
`SuperSocketLite2` 패키지를 참조한다. 그래서 이 디렉토리만 복사해 가도 그대로 빌드된다.

```xml
<PackageReference Include="SuperSocketLite2" Version="0.21.1" />
```

라이브러리를 고쳐 가며 시험할 때는 패키지 대신 `..\..\SuperSocketLite\SuperSocketLite.csproj`를
`ProjectReference`로 걸면 된다.
     
    
## GameServer_01
- 에코 기능만 구현 되어 있는 서버이다.
- Logger 라이브러리는 ZLogger를 사용하고 있다.
   

## GameServer_01_GenericHost
- `GameServer_01`에 `GenericHost` 기능을 추가해서 만든 것이다.
- 빌드 후 run_GameServer_01_GenericHost.bat 배치 파일로 실행한다. 
   
[Generic Host(일반 호스트) 소개 및 사용](https://jacking75.github.io/NET_GenericHost/)  | [MS Docs](https://learn.microsoft.com/ko-kr/dotnet/core/extensions/generic-host?tabs=appbuilder)     
    
  
  
## GameServer_MemoryPack
- 패킷 데이터 직렬화 라이브러리로 `MemoryPack`를 사용한다
- 빌드 후 run_GameServer_MemoryPack.bat 배치 파일로 실행한다.
- 테스트용 클라이언트는 `TestClient_MemoryPack`을 사용한다.  
    
  
## TestClient_MemoryPack
- `GameServer_MemoryPack`에 접속해서 패킷을 주고 받는 테스트용 클라이언트이다. WinForms로 만들었다.
- 서버와 같은 `MemoryPack` 패킷 정의(`PacketData.cs`, `PacketId.cs`)를 사용한다.  
    
  
## Protocol Buffers 서버
- 패킷 데이터 직렬화 라이브러리로 `Protocol.Buf`를 사용하는 서버이다
- 만들지는 않았다. `GameServer_MemoryPack` 와 `TestProtocolBuffer`(Test 디렉토리에 있다)을 참고하면 된다.