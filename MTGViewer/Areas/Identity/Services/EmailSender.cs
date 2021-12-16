using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;

namespace MTGViewer.Areas.Identity.Services;

public class AuthMessageSenderOptions
{
    public string SendGridKey { get; set; } = string.Empty;

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;
}


public class EmailSender : IEmailSender
{
    public EmailSender(IOptions<AuthMessageSenderOptions> optionsAccessor)
    {
        _options = optionsAccessor.Value;
    }

    private readonly AuthMessageSenderOptions _options;

    public Task SendEmailAsync(string email, string subject, string message)
    {
        var client = new SendGridClient(_options.SendGridKey);

        var msg = new SendGridMessage()
        {
            From = new EmailAddress(_options.SenderEmail, _options.SenderName),
            Subject = subject,
            PlainTextContent = message,
            HtmlContent = message
        };

        msg.AddTo(new EmailAddress(email));

        // Disable click tracking.
        // See https://sendgrid.com/docs/User_Guide/Settings/tracking.html
        msg.SetClickTracking(false, false);
        msg.SetOpenTracking(false);
        msg.SetGoogleAnalytics(false);
        msg.SetSubscriptionTracking(false);

        return client.SendEmailAsync(msg);
    }
}