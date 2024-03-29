using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SendGrid;
using SendGrid.Helpers.Mail;

namespace MtgViewer.Areas.Identity.Services;

public class SenderOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? EmailAlt { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class EmailSender : IEmailSender
{
    public EmailSender(IOptions<SenderOptions> optionsAccessor, ILogger<EmailSender> logger)
    {
        _options = optionsAccessor.Value;
        _logger = logger;
    }

    private readonly SenderOptions _options;
    private readonly ILogger<EmailSender> _logger;

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var client = new SendGridClient(_options.ApiKey);

        var msg = new SendGridMessage()
        {
            From = new EmailAddress(_options.Email, _options.Name),
            Subject = subject,
            PlainTextContent = htmlMessage,
            HtmlContent = htmlMessage
        };

        // issue where if the to and From are the same, the email is not sent
        if (email == _options.Email && _options.EmailAlt is not null)
        {
            email = _options.EmailAlt;
        }

        msg.AddTo(new EmailAddress(email));

        // Disable click tracking.
        // See https://sendgrid.com/docs/User_Guide/Settings/tracking.html
        msg.SetClickTracking(false, false);
        msg.SetOpenTracking(false);
        msg.SetGoogleAnalytics(false);
        msg.SetSubscriptionTracking(false);

        var result = await client.SendEmailAsync(msg);

        _logger.LogInformation("Email with response code {StatusCode}", result.StatusCode);
    }
}
