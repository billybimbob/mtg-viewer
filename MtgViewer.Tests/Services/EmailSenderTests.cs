using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;
using MtgViewer.Areas.Identity.Services;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace MtgViewer.Tests.Services;

public class EmailSenderTests
{
    private readonly IEmailSender _emailSender;
    private readonly AuthMessageSenderOptions _authOptions;

    public EmailSenderTests(
        IEmailSender emailSender, IOptions<AuthMessageSenderOptions> authOptions)
    {
        _emailSender = emailSender;
        _authOptions = authOptions.Value;
    }

    [Fact(Skip = "Calls external api")]
    public async Task SendEmail_PlainText_Success()
    {
        const string subject = "Test plaintext email";
        const string message = "Test if this email will send.";

        await _emailSender.SendEmailAsync(_authOptions.SenderEmail, subject, message);
    }

    [Fact(Skip = "Calls external api")]
    public async Task SendEmail_HtmlContent_Success()
    {
        const string subject = "Test html email";
        const string google = "google.com";

        string message = $"Test if this email <a href='{HtmlEncoder.Default.Encode(google)}'>with a link</a> will send";

        await _emailSender.SendEmailAsync(_authOptions.SenderEmail, subject, message);
    }
}
