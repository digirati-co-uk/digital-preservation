using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using MediatR;

namespace DigitalPreservation.Workspace.Requests;

public class AddItemsToMets(
    bool isBagItLayout,
    Uri depositFiles, 
    List<WorkingBase> items, 
    string depositETag) : IRequest<Result<ItemsAffected>>
{
    public bool IsBagItLayout { get; } = isBagItLayout;
    public Uri DepositFiles { get; } = depositFiles;
    public List<WorkingBase> Items { get; } = items;
    public string DepositETag { get; } = depositETag;
}

public class AddItemsToMetsHandler(IMetsManager metsManager) : IRequestHandler<AddItemsToMets, Result<ItemsAffected>>
{
    public async Task<Result<ItemsAffected>> Handle(AddItemsToMets request, CancellationToken cancellationToken)
    {
        FullMets? mets;
        var metsResult = await metsManager.GetFullMets(request.DepositFiles, request.DepositETag);
        if (metsResult is { Success: true, Value: not null })
        {
            mets = metsResult.Value;
        }
        else
        {
            return Result.FailNotNull<ItemsAffected>(
                metsResult.ErrorCode ?? ErrorCodes.UnknownError, metsResult.ErrorMessage);
        }

        bool metsHasBeenWrittenTo = false;
        var goodResult = new ItemsAffected();

        var rootRelativeItems = request.Items;
        if (request.IsBagItLayout)
        {
            rootRelativeItems = [];
            foreach (var item in request.Items)
            {
                if (item is WorkingDirectory directory)
                {
                    rootRelativeItems.Add(directory.ToRootLayout());
                }
                else if (item is WorkingFile file)
                {
                    rootRelativeItems.Add(file.ToRootLayout());
                }
            }
        }
        var shallowestFirst = rootRelativeItems
            .OrderBy(item => item.LocalPath.Count(c => c == '/'));
        foreach (var item in shallowestFirst)
        {
            var addResult = metsManager.AddToMets(mets, item);
            if (addResult.Success)
            {
                goodResult.Items.Add(new MinimalItem
                {
                    RelativePath = item.LocalPath,
                    IsDirectory = item is WorkingDirectory,
                    Whereabouts = Whereabouts.Mets
                });
                metsHasBeenWrittenTo = true;
            }
            else
            {
                return Result.FailNotNull<ItemsAffected>(
                    addResult.ErrorCode!,
                    $"MetsManager::AddToMets failed after {goodResult.Items.Count} items, " +
                            $"unable to write update METS file. {addResult.ErrorMessage}");
            }
        }
        
        if (mets != null && metsHasBeenWrittenTo)
        {
            var writeMetsResult = await metsManager.WriteMets(mets);
            if (writeMetsResult.Failure)
            {
                return Result.FailNotNull<ItemsAffected>(
                    writeMetsResult.ErrorCode!,
                    $"DeleteItems failed after {goodResult.Items.Count} items. Unable to update METS file.");
                
            }
        }
        return Result.OkNotNull(goodResult);
    }
}