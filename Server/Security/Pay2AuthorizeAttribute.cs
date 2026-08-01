using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Permissions;
using Microsoft.Extensions.DependencyInjection;

namespace Safir.Server.Security
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public class Pay2AuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public string Form { get; }
        public Pay2Perm Perm { get; }

        public Pay2AuthorizeAttribute(string form, Pay2Perm perm = Pay2Perm.Run)
        {
            Form = form;
            Perm = perm;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;
            if (!user.Identity?.IsAuthenticated ?? true)
            {
                context.Result = new UnauthorizedObjectResult("User not authenticated.");
                return;
            }

            var iddClaim = user.FindFirst(BaseknowClaimTypes.IDD) ?? user.FindFirst(ClaimTypes.NameIdentifier);
            if (iddClaim == null || !int.TryParse(iddClaim.Value, out int userCo))
            {
                context.Result = new UnauthorizedObjectResult("Invalid user identity.");
                return;
            }

            var accessService = context.HttpContext.RequestServices.GetRequiredService<IPay2AccessService>();

            bool hasAccess = await accessService.HasAsync(userCo, Form, (int)Perm);

            string? formCaption = Form;

            var auditEntry = new Pay2AuditEntry
            {
                UserCo = userCo,
                UserName = user.FindFirst(BaseknowClaimTypes.UUSER)?.Value,
                FormName = Form,
                PermFlag = Perm.ToString(),
                HttpMethod = context.HttpContext.Request.Method,
                Path = context.HttpContext.Request.Path,
                Ip = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                Allowed = hasAccess
            };

            await accessService.AuditAsync(auditEntry);

            if (!hasAccess)
            {
                string msg = $"دسترسی لازم برای این عملیات را ندارید. («{formCaption}» / {Perm})";
                context.Result = new ObjectResult(msg)
                {
                    StatusCode = 403
                };
            }
        }
    }
}
