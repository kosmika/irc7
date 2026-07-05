using Irc.Constants;
using Irc.Enumerations;
using Irc.Helpers;
using Irc.Interfaces;

namespace Irc.Commands;

public class Nick : Command, ICommand
{
    public Nick() : base(1, false)
    {
    }

    public new EnumCommandDataType GetDataType()
    {
        return EnumCommandDataType.Standard;
    }

    public new void Execute(IChatFrame chatFrame)
    {
        var hopcount = string.Empty;
        if (chatFrame.ChatMessage.Parameters.Count > 1) hopcount = chatFrame.ChatMessage.Parameters[1];

        // Is user not registered?
        // Set nickname according to regulations (should be available in user object and changes based on what they authenticated as)
        if (!chatFrame.User.IsAuthenticated()) HandlePreauthNicknameChange(chatFrame);
        else if (!chatFrame.User.IsRegistered()) HandlePreregNicknameChange(chatFrame);
        else HandleRegNicknameChange(chatFrame);
    }

    public static bool ValidateNickname(string nickname, bool guest = false, bool oper = false, bool preAuth = false,
        bool preReg = false, bool isDs = false, string? requiredPrefix = null)
    {
        var mask = Resources.PostAuthNicknameMask;

        if (preAuth) mask = Resources.PreAuthNicknameMask;
        else if (oper) mask = Resources.PostAuthOperNicknameMask;

        if (isDs) mask = Resources.DsNickname;

        var isPrefixValid = ValidatePrefix(nickname, requiredPrefix);

        // The nickname mask does not allow the prefix character, so strip a leading
        // required prefix before matching the regex against the actual nickname body.
        var nicknameBody = nickname;
        if (!string.IsNullOrEmpty(requiredPrefix) && nickname.Length > 0 && nickname[0] == requiredPrefix[0])
            nicknameBody = nickname.Substring(1);

        var isInLength = nickname.Length <= Resources.MaxFieldLen;
        var isMatch = RegularExpressions.Match(mask, nicknameBody, true);
        var isValid = isInLength && isMatch && isPrefixValid;
        return isValid;
    }

    /// <summary>
    /// Ensures the nickname begins with the required prefix character obtained from the
    /// authenticated user's credentials (DefaultPermissions.json prefix). When no prefix is
    /// required (empty/null), the nickname passes this check.
    /// </summary>
    private static bool ValidatePrefix(string nickname, string? requiredPrefix)
    {
        if (string.IsNullOrEmpty(requiredPrefix)) return true;
        return nickname.Length > 0 && nickname[0] == requiredPrefix[0];
    }

    /// <summary>
    /// Resolves the required nickname prefix for a user. Uses the prefix from the user's
    /// SASL credentials when authenticated; otherwise falls back to the global ANON prefix
    /// from DefaultPermissions.json (or a built-in default when ANON is not configured).
    /// </summary>
    public static string ResolveRequiredPrefix(IUser user)
    {
        var credentials = user.GetSspiHandler()?.GetCredentials();
        return credentials?.Prefix ?? Security.DefaultPermissions.AnonPrefix;
    }

    public static bool HandlePreauthNicknameChange(IChatFrame chatFrame)
    {
        var nickname = chatFrame.ChatMessage.Parameters.First();
        // UTF8 / Guest / Normal / Admin/Sysop/Guide OK
        var requiredPrefix = ResolveRequiredPrefix(chatFrame.User);
        var isValid = ValidateNickname(nickname, preAuth: true, isDs: chatFrame.Server.IsDirectoryServer,
            requiredPrefix: requiredPrefix); 
        if (!isValid)
        {
            chatFrame.User.Send(Raws.IRCX_ERR_ERRONEOUSNICK_432(chatFrame.Server, chatFrame.User, nickname));
            return false;
        }

        chatFrame.User.Nickname = nickname;
        return true;
    }

    public static bool HandlePreregNicknameChange(IChatFrame chatFrame)
    {
        var nickname = chatFrame.ChatMessage.Parameters.First();
        var guest = chatFrame.User.IsGuest();
        var oper = chatFrame.User.GetLevel() >= EnumUserAccessLevel.Guide;

        if (!ValidateNickname(nickname, guest, oper, false, true, isDs: chatFrame.Server.IsDirectoryServer,
                requiredPrefix: ResolveRequiredPrefix(chatFrame.User)))
        {
            chatFrame.User.Send(Raws.IRCX_ERR_ERRONEOUSNICK_432(chatFrame.Server, chatFrame.User, nickname));
            return false;
        }

        chatFrame.User.Nickname = nickname;
        return true;
    }

    public static bool HandleRegNicknameChange(IChatFrame chatFrame)
    {
        var nickname = chatFrame.ChatMessage.Parameters.First();
        var guest = chatFrame.User.IsGuest();
        var oper = chatFrame.User.GetLevel() >= EnumUserAccessLevel.Guide;

        if (!guest && !oper)
        {
            chatFrame.User.Send(Raws.IRCX_ERR_NONICKCHANGES_439(chatFrame.Server, chatFrame.User, nickname));
            return false;
        }

        var channels = chatFrame.User.GetChannels();
        foreach (var channel in channels)
        foreach (var member in channel.Key.GetMembers())
            if (member.GetUser().Nickname == nickname)
            {
                chatFrame.User.Send(Raws.IRCX_ERR_NICKINUSE_433(chatFrame.Server, chatFrame.User));
                return false;
            }

        if (!ValidateNickname(nickname, guest, oper, isDs: chatFrame.Server.IsDirectoryServer,
                requiredPrefix: ResolveRequiredPrefix(chatFrame.User)))
        {
            chatFrame.User.Send(Raws.IRCX_ERR_ERRONEOUSNICK_432(chatFrame.Server, chatFrame.User, nickname));
            return false;
        }

        chatFrame.User.ChangeNickname(nickname, false);
        return true;
    }
}