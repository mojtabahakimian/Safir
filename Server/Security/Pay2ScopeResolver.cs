using System.Threading.Tasks;
using Safir.Shared.Interfaces;
using Safir.Shared.Exceptions;
using System.Collections.Generic;
using System.Linq;

namespace Safir.Server.Security
{
    public enum Pay2ScopeKind
    {
        Workshop, Period, Run, Employee, Decree, Settlement, Loan, Leave, Contract, AdvanceExcl, Template
    }

    public class Pay2ScopeResolver
    {
        private readonly IDatabaseService _db;
        private readonly IPay2AccessService _access;

        public Pay2ScopeResolver(IDatabaseService db, IPay2AccessService access)
        {
            _db = db;
            _access = access;
        }

        public async Task<int?> ResolveWorkshopIdAsync(Pay2ScopeKind kind, long id)
        {
            string sql = kind switch
            {
                Pay2ScopeKind.Workshop => "SELECT @id",
                Pay2ScopeKind.Period => "SELECT WS_ID FROM dbo.PAY2_PERIOD WHERE PER_ID=@id",
                Pay2ScopeKind.Run => "SELECT P.WS_ID FROM dbo.PAY2_RUN R JOIN dbo.PAY2_PERIOD P ON R.PER_ID=P.PER_ID WHERE R.RUN_ID=@id",
                Pay2ScopeKind.Employee => "SELECT WS_ID FROM dbo.PAY2_EMPLOYEE WHERE EMP_ID=@id",
                Pay2ScopeKind.Decree => "SELECT WS_ID FROM dbo.PAY2_DECREE WHERE DEC_ID=@id",
                Pay2ScopeKind.Settlement => "SELECT WS_ID FROM dbo.PAY2_SETTLEMENT WHERE SET_ID=@id",
                Pay2ScopeKind.Loan => "SELECT WS_ID FROM dbo.PAY2_LOAN WHERE LOAN_ID=@id",
                Pay2ScopeKind.Leave => "SELECT E.WS_ID FROM dbo.PAY2_LEAVE L JOIN dbo.PAY2_EMPLOYEE E ON L.EMP_ID=E.EMP_ID WHERE L.LEV_ID=@id",
                Pay2ScopeKind.Contract => "SELECT E.WS_ID FROM dbo.PAY2_CONTRACT C JOIN dbo.PAY2_EMPLOYEE E ON C.EMP_ID=E.EMP_ID WHERE C.CON_ID=@id",
                Pay2ScopeKind.AdvanceExcl => "SELECT E.WS_ID FROM dbo.PAY2_ADVANCE_EXCL X JOIN dbo.PAY2_EMPLOYEE E ON X.EMP_ID=E.EMP_ID WHERE X.EXCL_ID=@id",
                Pay2ScopeKind.Template => "SELECT WS_ID FROM dbo.PAY2_ITEM_TEMPLATE WHERE TMPL_ID=@id",
                _ => null
            };

            if (sql == null) return null;

            var res = await _db.DoGetDataSQLAsync<int?>(sql, new { id });
            return res.FirstOrDefault();
        }

        public async Task EnsureWorkshopAsync(int userCo, Pay2ScopeKind kind, long id)
        {
            var wsId = await ResolveWorkshopIdAsync(kind, id);
            // If entity doesn't exist, we don't throw 403. Return and let the controller handle 404.
            // (Except if kind is template and wsId is null, it means global template, so allowed).
            if (kind == Pay2ScopeKind.Template && wsId == null) return;
            if (wsId == null) return;

            bool canAccess = await _access.CanAccessWorkshopAsync(userCo, wsId.Value);
            if (!canAccess)
            {
                throw new Pay2ForbiddenException("دسترسی لازم برای این عملیات را در کارگاه مربوطه ندارید.");
            }
        }

        public async Task EnsureWorkshopAsync(int userCo, int wsId)
        {
            bool canAccess = await _access.CanAccessWorkshopAsync(userCo, wsId);
            if (!canAccess)
            {
                throw new Pay2ForbiddenException("دسترسی لازم برای این کارگاه را ندارید.");
            }
        }
    }
}
