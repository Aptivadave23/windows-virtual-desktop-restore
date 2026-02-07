using System;

namespace StartUp.Services;

public sealed record LaunchResult(bool Success, int? ExitCode, string? Message, Exception? Exception)
{
    public static LaunchResult SuccessResult(string? message = null) => new(true, 0, message, null);
    public static LaunchResult Failure(string? message, int? exitCode = null, Exception? ex = null) => new(false, exitCode, message, ex);
    public static LaunchResult Ok(string? message = null) => SuccessResult(message);
}
