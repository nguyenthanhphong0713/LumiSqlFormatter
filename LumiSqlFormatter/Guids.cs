using System;

namespace LumiSqlFormatter
{
    public static class GuidList
    {
        public const string guidLumiSqlPackageString = "87E8E012-3F9C-4CD6-86A9-D97DDF8097E1";
        public const string guidLumiSqlCmdSetString = "D38D3551-7667-4B52-9A47-9759247E64B2";
        public const string guidSettingsToolWindowString = "984A2292-6FE7-4029-BA91-62885918A01A";

        public static readonly Guid guidLumiSqlPackage = new Guid(guidLumiSqlPackageString);
        public static readonly Guid guidLumiSqlCmdSet = new Guid(guidLumiSqlCmdSetString);
    }

    public static class PkgCmdIDList
    {
        public const uint cmdidOpenSettings = 0x0100;
        public const uint cmdidFormatUppercase = 0x0110;
        public const uint cmdidFormatLowercase = 0x0120;
        public const uint cmdidRefreshDbCache = 0x0130;
    }
}
