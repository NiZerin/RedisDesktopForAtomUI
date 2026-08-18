namespace RedisDesktop.Core;

public enum RedisDeploymentKind
{
    Standalone = 0,
    Sentinel = 1,
    Cluster = 2
}

public enum SessionState
{
    Idle,
    Connecting,
    Connected,
    Failed,
    Disposed
}

public enum RedisKeyType
{
    None,
    String,
    Hash,
    List,
    Set,
    SortedSet,
    Stream,
    ReJson,
    TimeSeries,
    Vector,
    Module,
    Unknown
}

public enum KeyTreeNodeKind
{
    Folder,
    Key
}

public enum KeyBrowseMode
{
    Tree,
    Flat
}
