namespace PlayerStatsSystem
{
    public interface IHitmarkerPreventer
    {
        bool TryPreventHitmarker(AttackerDamageHandler handler);
    }
}
