namespace FsrsSharp;

/// <summary>Base type for all errors thrown by FsrsSharp, mirroring ts-fsrs's FSRSError.</summary>
public class FsrsException : Exception
{
    public FsrsException(string message = "FSRS Error") : base(message) { }
}

/// <summary>Thrown when input (parameters, grade, date, ...) fails validation.</summary>
public class FsrsValidationException : FsrsException
{
    public FsrsValidationException(string message) : base(message) { }
}

/// <summary>Thrown when an operation is invalid for the current card or scheduler state, mirroring ts-fsrs's FSRSOperationError.</summary>
public class FsrsOperationException : FsrsException
{
    public FsrsOperationException(string message) : base(message) { }
}
