using Irc.Constants;
using Irc.Enumerations;
using Irc.Interfaces;
using Irc.Modes.User;
using Moq;

namespace Irc.Tests.Objects.User;

[TestFixture]
public class HostRuleTests
{
    [Test]
    public void Evaluate_DoesNotThrow_When_SourceNameIsBlank()
    {
        var user = CreateUser();
        user.Name = string.Empty; // blank source

        var rule = new HostRule();

        // Should not throw even when user has no channels and name is blank
        Assert.DoesNotThrow(() =>
        {
            var result = rule.Evaluate(user, user, true, string.Empty);
            Assert.That(result, Is.EqualTo(EnumIrcError.ERR_UNOTINCHANNEL));
        });
    }

    [Test]
    public void Evaluate_DoesNotThrow_When_TargetNameIsBlank()
    {
        var user = CreateUser();
        // target is same as source in this scenario but ensure the name is blank via ToString()/Name behavior
        var target = (IChatObject)user;
        target.Name = string.Empty; // blank target

        var rule = new HostRule();

        Assert.DoesNotThrow(() =>
        {
            var result = rule.Evaluate(user, target, true, string.Empty);
            Assert.That(result, Is.EqualTo(EnumIrcError.ERR_UNOTINCHANNEL));
        });
    }

    private static Irc.Objects.User.User CreateUser()
    {
        var mockConnection = new Mock<IConnection>();
        var mockProtocol = new Mock<IProtocol>();
        var mockDataRegulator = new Mock<IDataRegulator>();
        var mockFloodProtectionProfile = new Mock<IFloodProtectionProfile>();
        var mockServer = new Mock<IServer>();
        var mockSaslHandler = new Mock<ISaslHandler>();

        mockConnection.Setup(x => x.GetIp()).Returns("127.0.0.1");

        return new Irc.Objects.User.User(
            mockConnection.Object,
            mockProtocol.Object,
            mockDataRegulator.Object,
            mockFloodProtectionProfile.Object,
            mockServer.Object,
            _ => mockSaslHandler.Object)
        {
            Nickname = "TestUser"
        };
    }
}

