namespace PlayerRoles
{
    public static class Scp3114Compatibility
    {
        public static bool TryGetRoleTemplate<T>(this RoleTypeId role, out T template) where T : PlayerRoleBase
            => PlayerRoleLoader.TryGetRoleTemplate(role, out template);
    }
}
