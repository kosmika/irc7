using Irc.Commands;
using Irc.Enumerations;
using Irc.Interfaces;
using Irc.Objects.Channel;
using Irc.Objects.User;
using Moq;

namespace Irc.Tests.Commands;

/// <summary>
/// Tests for ESUBMIT, EPRIVMSG, and EQUESTION commands implementing the Special Guest (+g) mode protocol.
/// 
/// Protocol summary:
///   ESUBMIT  %#Channel :Question           — regular member submits a question; server delivers
///                                             it as EQUESTION only to channel hosts/owners.
///   EPRIVMSG %#Channel :Message            — host/owner sends a message to the whole channel
///                                             as the special guest's response.
///   EQUESTION %#To Nick %#From :Question   — host/owner forwards a question to a channel;
///                                             the raw broadcast includes both the to- and
///                                             from-channel so clients can display context.
/// </summary>
[TestFixture]
public class EsubmitEprivmsgEquestionTests
{
    private Mock<IServer> _mockServer = null!;

    [SetUp]
    public void SetUp()
    {
        _mockServer = new Mock<IServer>();
        _mockServer.Setup(s => s.ToString()).Returns("TestServer");
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    private Mock<IUser> CreateMockUser(string nick, string host = "anon", EnumUserAccessLevel level = EnumUserAccessLevel.None)
    {
        var mockProtocol = new Mock<IProtocol>();
        mockProtocol.Setup(p => p.GetProtocolType()).Returns(EnumProtocolType.IRC8);
        mockProtocol.Setup(p => p.GetFormat(It.IsAny<IUser>())).Returns(nick);
        mockProtocol.Setup(p => p.FormattedUser(It.IsAny<IChannelMember>())).Returns(nick);

        var address = new UserAddress();
        address.SetNickname(nick);
        address.User = nick;
        address.Host = host;
        address.RealName = nick;

        var user = new Mock<IUser>();
        user.Setup(u => u.ToString()).Returns(nick);
        user.Setup(u => u.Name).Returns(nick);
        user.Setup(u => u.GetAddress()).Returns(address);
        user.Setup(u => u.GetProtocol()).Returns(mockProtocol.Object);
        user.Setup(u => u.Send(It.IsAny<string>()));
        user.Setup(u => u.GetLevel()).Returns(level);
        user.Setup(u => u.IsAdministrator()).Returns(false);
        user.Setup(u => u.GetChannels()).Returns(new Dictionary<IChannel, IChannelMember>());
        user.Setup(u => u.Modes).Returns(new UserModes());
        user.Setup(u => u.Away).Returns(false);
        user.Setup(u => u.Server).Returns(_mockServer.Object);
        return user;
    }

    private static IChatFrame BuildFrame(IServer server, IUser user, IList<string> parameters)
    {
        var msg = new Mock<IChatMessage>();
        msg.Setup(m => m.Parameters).Returns(new List<string>(parameters));

        var frame = new Mock<IChatFrame>();
        frame.Setup(f => f.Server).Returns(server);
        frame.Setup(f => f.User).Returns(user);
        frame.Setup(f => f.ChatMessage).Returns(msg.Object);
        return frame.Object;
    }

    // -----------------------------------------------------------------------
    // ESUBMIT tests
    // -----------------------------------------------------------------------

    [Test]
    public void Esubmit_OnStageChannel_DeliversEquestionOnlyToHostsAndOwners()
    {
        // Arrange
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var submitter = CreateMockUser("AskingUser");
        var host = CreateMockUser("HostUser");
        var regularMember = CreateMockUser("RegularMember");

        channel.Join(submitter.Object, EnumChannelAccessResult.SUCCESS_GUEST);
        channel.Join(host.Object, EnumChannelAccessResult.SUCCESS_HOST);
        channel.Join(regularMember.Object, EnumChannelAccessResult.SUCCESS_GUEST);

        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sentToHost = new List<string>();
        var sentToRegular = new List<string>();
        host.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sentToHost.Add(m));
        regularMember.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sentToRegular.Add(m));

        var frame = BuildFrame(_mockServer.Object, submitter.Object, new[] { "%#OnStage", "Why am I here?" });

        // Act
        new Esubmit().Execute(frame);

        // Assert — EQUESTION must reach the host
        Assert.That(sentToHost, Has.Some.Contains("EQUESTION"),
            "Host should receive the EQUESTION from ESUBMIT.");

        // Assert — EQUESTION must NOT reach a regular member
        Assert.That(sentToRegular, Has.None.Contains("EQUESTION"),
            "Regular members must NOT receive EQUESTION from ESUBMIT.");
    }

    [Test]
    public void Esubmit_OnStageChannel_EquestionFormatIncludesFromChannel()
    {
        // Arrange
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var submitter = CreateMockUser("AskingUser");
        var host = CreateMockUser("HostUser");

        channel.Join(submitter.Object, EnumChannelAccessResult.SUCCESS_GUEST);
        channel.Join(host.Object, EnumChannelAccessResult.SUCCESS_HOST);

        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sentToHost = new List<string>();
        host.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sentToHost.Add(m));

        var frame = BuildFrame(_mockServer.Object, submitter.Object, new[] { "%#OnStage", "My question" });

        // Act
        new Esubmit().Execute(frame);

        // Assert — raw should contain to-channel, asker nick, and from-channel (same for ESUBMIT)
        var equestionRaw = sentToHost.FirstOrDefault(m => m.Contains("EQUESTION"));
        Assert.That(equestionRaw, Is.Not.Null, "Host should receive an EQUESTION raw.");
        // Format: :{addr} EQUESTION %#OnStage AskingUser %#OnStage :My question
        Assert.That(equestionRaw, Does.Contain("EQUESTION %#OnStage AskingUser %#OnStage :My question"),
            "EQUESTION raw must include the submitter nick and the from-channel.");
    }

    [Test]
    public void Esubmit_UserNotOnChannel_Sends442()
    {
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var outsider = CreateMockUser("Outsider");
        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sent = new List<string>();
        outsider.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, outsider.Object, new[] { "%#OnStage", "My question" });
        new Esubmit().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 442 "), "Non-member must receive ERR_NOTONCHANNEL (442).");
    }

    [Test]
    public void Esubmit_ChannelWithoutOnStageMode_Sends404()
    {
        var channel = new Channel("%#Normal");
        // OnStage NOT set

        var user = CreateMockUser("AskingUser");
        channel.Join(user.Object, EnumChannelAccessResult.SUCCESS_GUEST);
        _mockServer.Setup(s => s.GetChannelByName("%#Normal")).Returns(channel);

        var sent = new List<string>();
        user.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, user.Object, new[] { "%#Normal", "My question" });
        new Esubmit().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 404 "), "Channel without +g must return ERR_CANNOTSENDTOCHAN (404).");
    }

    // -----------------------------------------------------------------------
    // EPRIVMSG tests
    // -----------------------------------------------------------------------

    [Test]
    public void Eprivmsg_HostOnOnStageChannel_BroadcastsToWholeChannel()
    {
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var host = CreateMockUser("HostUser", level: EnumUserAccessLevel.Guide);
        var member = CreateMockUser("RegularMember");

        channel.Join(host.Object, EnumChannelAccessResult.SUCCESS_HOST);
        channel.Join(member.Object, EnumChannelAccessResult.SUCCESS_GUEST);

        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sentToMember = new List<string>();
        member.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sentToMember.Add(m));

        var frame = BuildFrame(_mockServer.Object, host.Object, new[] { "%#OnStage", "The answer is 42." });
        new Eprivmsg().Execute(frame);

        Assert.That(sentToMember, Has.Some.Contains("EPRIVMSG"),
            "Regular members must receive the EPRIVMSG sent by a host.");
    }

    [Test]
    public void Eprivmsg_RegularMemberOnOnStageChannel_Sends404()
    {
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var regular = CreateMockUser("RegularMember");
        channel.Join(regular.Object, EnumChannelAccessResult.SUCCESS_GUEST);

        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sent = new List<string>();
        regular.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, regular.Object, new[] { "%#OnStage", "I am not allowed." });
        new Eprivmsg().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 404 "),
            "Regular members must receive ERR_CANNOTSENDTOCHAN (404) when trying to send EPRIVMSG.");
        Assert.That(sent, Has.None.Contains("EPRIVMSG "),
            "No EPRIVMSG broadcast should occur for a non-host sender.");
    }

    [Test]
    public void Eprivmsg_UserNotOnChannel_Sends442()
    {
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var outsider = CreateMockUser("Outsider");
        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sent = new List<string>();
        outsider.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, outsider.Object, new[] { "%#OnStage", "No access." });
        new Eprivmsg().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 442 "), "Non-member must receive ERR_NOTONCHANNEL (442).");
    }

    [Test]
    public void Eprivmsg_ChannelWithoutOnStageMode_Sends404()
    {
        var channel = new Channel("%#Normal");

        var host = CreateMockUser("HostUser");
        channel.Join(host.Object, EnumChannelAccessResult.SUCCESS_HOST);
        _mockServer.Setup(s => s.GetChannelByName("%#Normal")).Returns(channel);

        var sent = new List<string>();
        host.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, host.Object, new[] { "%#Normal", "Not +g channel." });
        new Eprivmsg().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 404 "), "Channel without +g must return ERR_CANNOTSENDTOCHAN (404).");
    }

    // -----------------------------------------------------------------------
    // EQUESTION tests
    // -----------------------------------------------------------------------

    [Test]
    public void Equestion_HostForwardsQuestion_BroadcastsToChannelWithCorrectFormat()
    {
        var channel = new Channel("%#Onstage3");
        channel.Modes.OnStage.ModeValue = true;

        var moderator = CreateMockUser("DishDiva", "cg", level: EnumUserAccessLevel.Guide);
        var audience = CreateMockUser("AudienceMember");

        channel.Join(moderator.Object, EnumChannelAccessResult.SUCCESS_HOST);
        channel.Join(audience.Object, EnumChannelAccessResult.SUCCESS_GUEST);

        _mockServer.Setup(s => s.GetChannelByName("%#Onstage3")).Returns(channel);

        var sentToAudience = new List<string>();
        audience.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sentToAudience.Add(m));

        // Moderator forwards: EQUESTION %#Onstage3 Auntiehoo %#Onstage1 :Is there anyone...
        var frame = BuildFrame(_mockServer.Object, moderator.Object,
            new[] { "%#Onstage3", "Auntiehoo", "%#Onstage1", "Is there anyone you would like to work with?" });

        new Equestion().Execute(frame);

        var equestionRaw = sentToAudience.FirstOrDefault(m => m.Contains("EQUESTION"));
        Assert.That(equestionRaw, Is.Not.Null, "Audience members should receive the EQUESTION broadcast.");

        // Format: :DishDiva!DishDiva@cg EQUESTION %#Onstage3 Auntiehoo %#Onstage1 :Is there anyone...
        Assert.That(equestionRaw, Does.Contain("EQUESTION %#Onstage3 Auntiehoo %#Onstage1 :Is there anyone you would like to work with?"),
            "EQUESTION raw must include the to-channel, asker nick, from-channel, and question text.");
    }

    [Test]
    public void Equestion_RegularMemberOnOnStageChannel_Sends404()
    {
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var regular = CreateMockUser("RegularMember");
        channel.Join(regular.Object, EnumChannelAccessResult.SUCCESS_GUEST);

        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sent = new List<string>();
        regular.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, regular.Object,
            new[] { "%#OnStage", "SomeNick", "%#FromChannel", "My question" });
        new Equestion().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 404 "),
            "Regular members must receive ERR_CANNOTSENDTOCHAN (404) when trying to send EQUESTION.");
    }

    [Test]
    public void Equestion_UserNotOnChannel_Sends442()
    {
        var channel = new Channel("%#OnStage");
        channel.Modes.OnStage.ModeValue = true;

        var outsider = CreateMockUser("Outsider");
        _mockServer.Setup(s => s.GetChannelByName("%#OnStage")).Returns(channel);

        var sent = new List<string>();
        outsider.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, outsider.Object,
            new[] { "%#OnStage", "SomeNick", "%#FromChannel", "My question" });
        new Equestion().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 442 "), "Non-member must receive ERR_NOTONCHANNEL (442).");
    }

    [Test]
    public void Equestion_ChannelWithoutOnStageMode_Sends404()
    {
        var channel = new Channel("%#Normal");

        var host = CreateMockUser("HostUser");
        channel.Join(host.Object, EnumChannelAccessResult.SUCCESS_HOST);
        _mockServer.Setup(s => s.GetChannelByName("%#Normal")).Returns(channel);

        var sent = new List<string>();
        host.Setup(u => u.Send(It.IsAny<string>())).Callback((string m) => sent.Add(m));

        var frame = BuildFrame(_mockServer.Object, host.Object,
            new[] { "%#Normal", "SomeNick", "%#FromChannel", "My question" });
        new Equestion().Execute(frame);

        Assert.That(sent, Has.Some.Contains(" 404 "), "Channel without +g must return ERR_CANNOTSENDTOCHAN (404).");
    }
}
