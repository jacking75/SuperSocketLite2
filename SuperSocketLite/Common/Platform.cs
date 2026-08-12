using System;


namespace SuperSocketLite.Common;

/// <summary>
/// This class is designed for detect platform attribute in runtime
/// </summary>
public static class Platform
{
    static Platform()
    {
#pragma warning disable CS0618 // the property is kept for binary compatibility only
        //IOControlCode based probing is Windows-only; on every other platform the raw ioctl
        //throws at call time rather than here.
        SupportSocketIOControlByCodeEnum = OperatingSystem.IsWindows();
#pragma warning restore CS0618

        Type? t = Type.GetType("Mono.Runtime");
        IsMono = t != null;
    }

    /// <summary>
    /// Gets a value indicating whether [support socket IO control by code enum].
    /// </summary>
    /// <value>
    /// 	<c>true</c> if [support socket IO control by code enum]; otherwise, <c>false</c>.
    /// </value>
    [Obsolete("The library no longer uses IOControl for socket configuration. Use OperatingSystem.IsWindows() if a Windows-only ioctl is needed.")]
    public static bool SupportSocketIOControlByCodeEnum { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this instance is mono.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is mono; otherwise, <c>false</c>.
    /// </value>
    public static bool IsMono { get; private set; }
}
