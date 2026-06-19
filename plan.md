1. **Update `Pay2DecreeLineDto.cs` & `Pay2ItemTmplLineDto` (in `Pay2EmployeeModels.cs`)**:
   - Change the `AMOUNT` type to `decimal`.
   - Change the `DEF_AMOUNT` type to `decimal`.

2. **Update `ScriptSqly.cs`**:
   - In `SP_PAY2_CALC_RUN` logic within the `ScriptSqly.cs` string definition:
     - Change `@ITEM_AMOUNT BIGINT` to `@ITEM_AMOUNT DECIMAL(18,2)`.
   - Update `PAY2_DDL` logic within `ScriptSqly.cs`:
     - Modify `PAY2_DECREE_LINE` table definition `AMOUNT BIGINT` to `AMOUNT DECIMAL(18,2)`.
     - Modify `PAY2_ITEM_TMPL_LINE` table definition `DEF_AMOUNT BIGINT` to `DEF_AMOUNT DECIMAL(18,2)`.

3. **Create Database Migration `008_Pay2_DecimalAmount.sql`**:
   - `ALTER TABLE PAY2_DECREE_LINE ALTER COLUMN AMOUNT DECIMAL(18,2) NOT NULL;` (Make sure to drop constraint `DF_DL_AMT` first and then recreate it).
   - `ALTER TABLE PAY2_ITEM_TMPL_LINE ALTER COLUMN DEF_AMOUNT DECIMAL(18,2) NOT NULL;` (Make sure to drop constraint `DF_TL_AMT` first and then recreate it).
   - Update the `SP_PAY2_CALC_RUN` stored procedure to use `@ITEM_AMOUNT DECIMAL(18,2)`.

4. **Update `DecreeLineModal.razor`**:
   - Change parsing of `CurrentAmountStr` from `long.TryParse` to `decimal.TryParse`.
   - Ensure the UI handles percentage inputs correctly (e.g. `10.5` instead of `10`).

5. **Pre-commit checks**: Run `pre_commit_instructions` tool.
