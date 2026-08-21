using Microsoft.CodeAnalysis;

namespace SuperSocketLite.Analyzers;

/// <summary>컴파일마다 한 번만 찾아 두는 심볼들.</summary>
internal sealed class KnownTypes
{
    public const string RequestInfoMetadataName = "SuperSocketLite.SocketBase.Protocol.IRequestInfo";
    public const string ReadOnlySequenceMetadataName = "System.Buffers.ReadOnlySequence`1";
    public const string ReadOnlyMemoryMetadataName = "System.ReadOnlyMemory`1";
    public const string MemoryMetadataName = "System.Memory`1";
    public const string AppSessionMetadataName = "SuperSocketLite.SocketBase.AppSession`2";
    public const string AppSessionInterfaceMetadataName = "SuperSocketLite.SocketBase.IAppSession";
    public const string AppServerBaseMetadataName = "SuperSocketLite.SocketBase.AppServerBase`2";
    public const string ArrayPoolMetadataName = "System.Buffers.ArrayPool`1";

    private KnownTypes(
        INamedTypeSymbol requestInfo,
        INamedTypeSymbol? readOnlySequence,
        INamedTypeSymbol? readOnlyMemory,
        INamedTypeSymbol? memory)
    {
        RequestInfo = requestInfo;
        ReadOnlySequence = readOnlySequence;
        ReadOnlyMemory = readOnlyMemory;
        Memory = memory;
    }

    public INamedTypeSymbol RequestInfo { get; }
    public INamedTypeSymbol? ReadOnlySequence { get; }
    public INamedTypeSymbol? ReadOnlyMemory { get; }
    public INamedTypeSymbol? Memory { get; }

    /// <summary>
    /// SuperSocketLite 를 참조하지 않는 컴파일이면 null 을 돌려준다. 그러면 애널라이저는 아무 일도 하지 않는다.
    /// </summary>
    public static KnownTypes? TryCreate(Compilation compilation)
    {
        var requestInfo = compilation.GetTypeByMetadataName(RequestInfoMetadataName);

        if (requestInfo is null)
        {
            return null;
        }

        return new KnownTypes(
            requestInfo,
            compilation.GetTypeByMetadataName(ReadOnlySequenceMetadataName),
            compilation.GetTypeByMetadataName(ReadOnlyMemoryMetadataName),
            compilation.GetTypeByMetadataName(MemoryMetadataName));
    }

    /// <summary>핸들러 밖으로 새어 나가면 안 되는 타입인가.</summary>
    /// <remarks>
    /// <c>request.PacketId</c> 처럼 값만 복사되는 멤버는 저장해도 안전하므로 걸러 낸다.
    /// 위험한 건 요청 인스턴스 자체와, 수신 파이프의 메모리를 가리키는 시퀀스/메모리다.
    /// </remarks>
    public bool IsLifetimeBoundType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (Implements(type, RequestInfo))
        {
            return true;
        }

        if (type is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return false;
        }

        var definition = named.OriginalDefinition;

        return SymbolEqualityComparer.Default.Equals(definition, ReadOnlySequence)
            || SymbolEqualityComparer.Default.Equals(definition, ReadOnlyMemory)
            || SymbolEqualityComparer.Default.Equals(definition, Memory);
    }

    public static bool Implements(ITypeSymbol type, INamedTypeSymbol @interface)
    {
        if (SymbolEqualityComparer.Default.Equals(type, @interface))
        {
            return true;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, @interface))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="type"/> 자신이나 그 기반 타입 중에 <paramref name="metadataName"/> 이 있는가.
    /// 제네릭은 원본 정의로 비교한다.
    /// </summary>
    public static bool DerivesFrom(ITypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (GetMetadataName(current) == metadataName)
            {
                return true;
            }
        }

        if (type is null)
        {
            return false;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (GetMetadataName(implemented) == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetMetadataName(ITypeSymbol type)
    {
        var definition = type.OriginalDefinition;

        if (definition.ContainingNamespace is null || definition.ContainingNamespace.IsGlobalNamespace)
        {
            return definition.MetadataName;
        }

        return definition.ContainingNamespace.ToDisplayString() + "." + definition.MetadataName;
    }
}
