namespace HeatmapTool
{
    public interface IHeatmapStateProvider
    {
        bool IsGrounded { get; }
        bool IsSprinting { get; }
    }
}
