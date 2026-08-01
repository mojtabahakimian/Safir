using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Safir.Shared.Exceptions;

namespace Safir.Server.Security
{
    public class Pay2ForbiddenFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is Pay2ForbiddenException ex)
            {
                context.Result = new ObjectResult(ex.Message)
                {
                    StatusCode = 403
                };
                context.ExceptionHandled = true;
            }
        }
    }
}
