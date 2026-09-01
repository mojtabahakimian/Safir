using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading.Tasks;

namespace Safir.Server.Services
{
    public class SmtpSettings
    {
        [Required] public string SmtpHost { get; set; } = "";
        [Range(1, 65535)] public int SmtpPort { get; set; } = 587;
        [Required] public string Username { get; set; } = "";
        [Required] public string Password { get; set; } = "";
        [Required] public string SenderEmail { get; set; } = "";
        public string SenderName { get; set; } = "Safir";
        [Required] public string ReceiverEmail { get; set; } = "";
    }

    public interface IEmailService
    {
        Task SendAsync(string subject, string htmlBody);
    }

    public class EmailService : IEmailService
    {
        private readonly SmtpSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<SmtpSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task SendAsync(string subject, string htmlBody)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(_settings.ReceiverEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, SecureSocketOptions.StartTls);
            try
            {
                await client.AuthenticateAsync(_settings.Username, _settings.Password);
                await client.SendAsync(message);
                _logger.LogInformation("Email sent: {Subject}", subject);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }
    }
}
