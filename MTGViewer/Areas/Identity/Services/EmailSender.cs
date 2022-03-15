using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;

namespace MTGViewer.Areas.Identity.Services;

public class AuthMessageSenderOptions
{
    public string SendGridKey { get; set; } = string.Empty;

    public string SenderEmail { get; set; } = string.Empty;

    public string? SenderEmailAlt { get; set; }

    public string SenderName { get; set; } = string.Empty;
}


public class EmailSender : IEmailSender
{
    public EmailSender(IOptions<AuthMessageSenderOptions> optionsAccessor, ILogger<EmailSender> logger)
    {
        _options = optionsAccessor.Value;
        _logger = logger;
    }

    private readonly AuthMessageSenderOptions _options;
    private readonly ILogger<EmailSender> _logger;


    public async Task SendEmailAsync(string email, string subject, string message)
    {
        var client = new SendGridClient(_options.SendGridKey);

        var msg = new SendGridMessage()
        {
            From = new EmailAddress(_options.SenderEmail, _options.SenderName),
            Subject = subject,
            PlainTextContent = message,
            HtmlContent = message
        };

        // issue where if the to and From are the same, the email is not sent
        if (email == _options.SenderEmail && _options.SenderEmailAlt is not null)
        {
            email = _options.SenderEmailAlt;
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