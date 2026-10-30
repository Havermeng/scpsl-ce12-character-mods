public static class HitmarkerPolicy
{
    public static bool CanShow(IDestructible target, PlayerStatsSystem.AttackerDamageHandler handler)
    {
        return !ReferenceHub.TryGetHubNetID(target.NetworkId, out var victim) ||
            !(victim.roleManager.CurrentRole is PlayerStatsSystem.IHitmarkerPreventer preventer) ||
            !preventer.TryPreventHitmarker(handler);
    }
}
