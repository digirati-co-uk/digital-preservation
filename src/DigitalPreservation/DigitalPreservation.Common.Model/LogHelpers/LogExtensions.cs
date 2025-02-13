using System.Text;
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PreservationApi;

namespace DigitalPreservation.Common.Model.LogHelpers;

public static class LogExtensions
{
    public static string LogSummary(this Deposit? deposit)
    {
        if (deposit is null)
        {
            return "NULL DEPOSIT";
        }
        
        var sb = new StringBuilder("[Deposit - Id: ");
        sb.Append(deposit.Id);
        sb.Append(", AG: ");
        sb.Append(deposit.ArchivalGroup);
        sb.Append(", Active: ");
        sb.Append(deposit.Active);
        sb.Append(", Status: ");
        sb.Append(deposit.Status);
        sb.Append(", vEx: ");
        sb.Append(deposit.VersionExported);
        sb.Append(", vPr: ");
        sb.Append(deposit.VersionPreserved);
        sb.Append(" ]");
        return sb.ToString();
    }


    public static string LogSummary(this ImportJob? importJob)
    {
        if (importJob is null)
        {
            return "NULL importJob";
        }

        var sb = new StringBuilder("[ImportJob - Id: ");
        sb.Append(importJob.Id);
        sb.Append(", OriginalId: ");
        sb.Append(importJob.OriginalId);
        sb.Append(", AG: ");
        sb.Append(importJob.ArchivalGroup);
        sb.Append(", isUpdate: ");
        sb.Append(importJob.IsUpdate);
        sb.Append(", Deposit: ");
        sb.Append(importJob.Deposit);
        sb.Append(", Source: ");
        sb.Append(importJob.Source);
        sb.Append(", version: ");
        sb.Append(importJob.SourceVersion?.OcflVersion);
        sb.Append(", b+: ");
        sb.Append(importJob.BinariesToAdd.Count);
        sb.Append(", b-: ");
        sb.Append(importJob.BinariesToDelete.Count);
        sb.Append(", bp: ");
        sb.Append(importJob.BinariesToPatch.Count);
        sb.Append(", br: ");
        sb.Append(importJob.BinariesToRename.Count);
        sb.Append(", c+: ");
        sb.Append(importJob.ContainersToAdd.Count);
        sb.Append(", c-: ");
        sb.Append(importJob.ContainersToDelete.Count);
        sb.Append(", cr: ");
        sb.Append(importJob.ContainersToRename.Count);
        sb.Append(" ]");
        return sb.ToString();
    }
    
    public static string LogSummary(this ImportJobResult? importJobResult)
    {
        if (importJobResult is null)
        {
            return "NULL importJobResult";
        }
        
        var sb = new StringBuilder("[ImportJobResult - Id: ");
        sb.Append(importJobResult.Id);
        sb.Append(", ImportJob: ");
        sb.Append(importJobResult.ImportJob);
        sb.Append(", OriginalImportJob: ");
        sb.Append(importJobResult.OriginalImportJob);
        sb.Append(", AG: ");
        sb.Append(importJobResult.ArchivalGroup);
        sb.Append(", Deposit: ");
        sb.Append(importJobResult.Deposit);
        sb.Append(", status: ");
        sb.Append(importJobResult.Status);
        sb.Append(", sourceVersion: ");
        sb.Append(importJobResult.SourceVersion);
        sb.Append(", newVersion: ");
        sb.Append(importJobResult.NewVersion);
        sb.Append(", errors: ");
        sb.Append(importJobResult.Errors?.Length);
        sb.Append(", b+: ");
        sb.Append(importJobResult.BinariesAdded.Count);
        sb.Append(", b-: ");
        sb.Append(importJobResult.BinariesDeleted.Count);
        sb.Append(", bp: ");
        sb.Append(importJobResult.BinariesPatched.Count);
        sb.Append(", br: ");
        sb.Append(importJobResult.BinariesRenamed.Count);
        sb.Append(", c+: ");
        sb.Append(importJobResult.ContainersAdded.Count);
        sb.Append(", c-: ");
        sb.Append(importJobResult.ContainersDeleted.Count);
        sb.Append(", cr: ");
        sb.Append(importJobResult.ContainersRenamed.Count);
        sb.Append(" ]");
        return sb.ToString();
    }
}