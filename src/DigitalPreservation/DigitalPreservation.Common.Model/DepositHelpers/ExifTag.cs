using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DigitalPreservation.Common.Model.DepositHelpers;
public class ExifTag
{
    public string? TagName { get; set; } = string.Empty;
    public string? TagValue { get; set; } = string.Empty;
    public bool? MismatchAdded { get; set; } = false;
}
