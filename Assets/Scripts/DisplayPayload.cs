using MessagePack;

namespace SMJV
{
    [MessagePackObject]
    public class DisplayPayload
    {
        [Key("label")] public string label;
        [Key("value")] public string value;
    }
}
