namespace Workflow.Domain.Enums;

public enum ActivityType
{
    Start              = 0,
    End                = 1,
    UserTask           = 2,
    ExclusiveGateway   = 3,
    ServiceTask        = 4,
    Timer              = 5,
    NotificationTask   = 6,
    ParallelGateway    = 7,
    JoinGateway        = 8,
    ScriptTask         = 9,   // SAFE: only sets workflow variables via whitelist expressions — NO arbitrary code
    WaitEvent          = 10,  // waits for external signal / correlation
    InclusiveGateway   = 11,  // OR-split: start ALL matching condition transitions; if none match, take default
    CallActivity       = 12,  // start child published definition, optionally wait for completion
    MainActivity       = 13,  // required child workflow, then an assigned approval
}

