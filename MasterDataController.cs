
using System.Collections.Generic;
using System.Security.Claims;
using Azure.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SafetyToolBoxAPI.Common;
using SafetyToolBoxDL.CommonDB;
using SafetyToolBoxDL.CustomDB;
using SafetyToolBoxDL.HRMSDB;
using SafetyToolBoxDL.SafetyDB;
using static SafetyToolBoxAPI.Common.BranchInfo;
using static SafetyToolBoxAPI.Common.RejectMissPunchRequestDto;


namespace SafetyToolBoxAPI.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    [Authorize]
    public class MasterDataController : ControllerBase
    {
        private readonly VivifyHrmsContext HRMSDBContext;
        private readonly CustomDBHRMSContext HRMSCustomDBContext;
        private readonly VivifyCommonContext CommonDBContext;
        private readonly VivifySafetyToolboxContext SafetyDBContext;


        private IConfiguration Config;

        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public MasterDataController(VivifyHrmsContext _HRMSDBContext, IConfiguration _config, CustomDBHRMSContext _CustomDBHRMSContext, VivifyCommonContext _commonDBContext)
        {
            HRMSDBContext = _HRMSDBContext;
            HRMSCustomDBContext = _CustomDBHRMSContext;
            CommonDBContext = _commonDBContext;
            Config = _config;
        }
        [HttpGet]
        [ActionName("GetAllBranches")]
        public VivifyResponse<List<BranchInfo>> GetAllBranches(int? branchId = null)
        {
            try
            {
                var branches = (from b in CommonDBContext.MBranches
                                join st in CommonDBContext.MSiteTypes
                                on b.SiteType equals st.Id into siteGroup
                                from site in siteGroup.DefaultIfEmpty()
                                where (branchId == null || b.Id == branchId)
                                                            && b.IsActive == true
                                select new BranchInfo
                                {
                                    Id = b.Id,
                                    BranchCode = b.BranchCode,
                                    BranchName = b.BranchDesc,
                                    GeoFencing = b.Geofencing,
                                    IsActive = b.IsActive,

                                    SiteId = b.SiteType,
                                    SiteDesc = site.SiteDescription 
                                }).ToList();

                if (branches == null || branches.Count == 0)
                {
                    return new VivifyResponse<List<BranchInfo>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No branches found"
                    };
                }

                return new VivifyResponse<List<BranchInfo>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = branches
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<BranchInfo>>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }

        [HttpGet]
        [ActionName("GetAllDesignation")]
        public VivifyResponse<List<Designation>> GetAllDesignation()
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                List<Designation> lstMBranches = CommonDBContext.MDesignations.Where(x=>x.CompanyId== CompanyID).Select(a=> new Designation {ID=a.Id,DesignationId=a.DesignationId,DesignationDesc=a.DesignationDesc}).ToList();

                return new VivifyResponse<List<Designation>> { StatusCode = 200, StatusDesc = "Loded Successfully", Result = lstMBranches };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<Designation>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }
        [HttpGet]
        [ActionName("GetAllShifts")]
        public VivifyResponse<List<ShiftDetails>> GetAllShifts(long? empNo = null, string branchCode = null)
        {
            try
            {
                long LoggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

              
                var empShifts = HRMSDBContext.EmpShiftHours.ToList();
                var shiftHours = HRMSDBContext.MShiftHours.ToList();
                var employees = CommonDBContext.EmployeeInfos.ToList();
                var branches = CommonDBContext.MBranches.ToList();
                var designations = CommonDBContext.MDesignations.ToList();  

               
                var lstShifts = (from esh in empShifts
                                 join ei in employees on esh.EmpNo equals ei.EmpNo
                                 join b in branches on ei.BranchCode equals b.BranchCode
                                 join sh in shiftHours on esh.ShiftId equals sh.ShiftId
                                 join d in designations on ei.Designation equals d.DesignationId into desig
                                 from d in desig.DefaultIfEmpty() 
                                 where
                                     (empNo == null || ei.EmpNo == empNo) &&
                                     (string.IsNullOrEmpty(branchCode) || ei.BranchCode == branchCode)
                                 select new ShiftDetails
                                 {
                                     EmpNo = ei.EmpNo,
                                     FirstName = $"{ei.EmpNo} - {ei.FirstName}",
                                     LastName = ei.LastName,
                                     Designation = ei.Designation,  
                                     DesignationDesc = d != null ? d.DesignationDesc : " ",  
                                     BranchCode = ei.BranchCode,
                                     BranchDesc = b.BranchDesc,
                                     ShiftId = esh.ShiftId,
                                     ShiftName = sh.ShiftName
                                 }).ToList();

               
                return new VivifyResponse<List<ShiftDetails>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = lstShifts
                };
            }
            catch (Exception ex)
            {
               
                return new VivifyResponse<List<ShiftDetails>>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }

        [HttpGet]
        [ActionName("GetShiftTypes")]
        public VivifyResponse<List<ShiftTypes>> GetShiftTypes()
        {
            try
            {
                var shifts = (from sh in HRMSDBContext.MShiftHours
                              where sh.IsActive == true 
                              select new ShiftTypes
                              {
                                  ShiftId = sh.ShiftId,
                                  ShiftName = sh.ShiftName,
                                  WorkingHours = sh.WorkingHours
                              }).ToList();

                if (!shifts.Any()) 
                {
                    return new VivifyResponse<List<ShiftTypes>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active shifts found"
                    };
                }

                return new VivifyResponse<List<ShiftTypes>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = shifts
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<ShiftTypes>>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }

        [HttpGet]
        [ActionName("GetSiteTypes")]
        public VivifyResponse<List<SiteTypes>> GetSiteTypes()
        {
            try
            {
                var sites = (from st in CommonDBContext.MSiteTypes
                             where st.IsActive == true   
                             select new SiteTypes
                             {
                                 SiteId = st.Id,              
                                 SiteTypeId = st.SiteTypeId,   
                                 SiteDesc = st.SiteDescription 
                             }).ToList();

                if (!sites.Any())
                {
                    return new VivifyResponse<List<SiteTypes>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No sites found",
                        Result = new List<SiteTypes>()
                    };
                }

                return new VivifyResponse<List<SiteTypes>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = sites
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<SiteTypes>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new List<SiteTypes>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetAdvanceTypes")]
        public VivifyResponse<List<AdvanceTypeDTO>> GetAdvanceTypes()
        {
            try
            {
                var advanceTypes = (from at in HRMSDBContext.AdvanceTypes
                                    where at.IsActive == true   
                                    select new AdvanceTypeDTO
                                    {
                                        AdvanceTypeId = at.AdvanceTypeID,
                                        AdvanceDesc = at.AdvanceDesc
                                    }).ToList();

                if (!advanceTypes.Any())
                {
                    return new VivifyResponse<List<AdvanceTypeDTO>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No advance types found",
                        Result = new List<AdvanceTypeDTO>()
                    };
                }

                return new VivifyResponse<List<AdvanceTypeDTO>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = advanceTypes
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<AdvanceTypeDTO>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new List<AdvanceTypeDTO>()
                };
            }
        }
      


        [HttpGet]
        [ActionName("GetShiftTypeInfo")]
        public VivifyResponse<List<GetShiftTypeInfo>> GetShiftTypeInfo(int? shiftId = null)
        {
            try
            {
                var shifts = (from sh in HRMSDBContext.MShiftHours
                              where shiftId == null || sh.ShiftId == shiftId
                              select new GetShiftTypeInfo 
                              {
                                  ShiftId = sh.ShiftId,
                                  ShiftName = sh.ShiftName,
                                  WorkingHours = sh.WorkingHours
                              }).ToList();

                if (shifts == null || shifts.Count == 0)
                {
                    return new VivifyResponse<List<GetShiftTypeInfo>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No shifts found"
                    };
                }

                return new VivifyResponse<List<GetShiftTypeInfo>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = shifts
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<GetShiftTypeInfo>>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }
        [HttpGet]
        [ActionName("GetDesignationInfo")]
        public VivifyResponse<List<Designation>> GetDesignationInfo(int? designationId = null)
        {
            try
            {
               
                var designations = (from d in CommonDBContext.MDesignations
                                    where designationId == null || d.DesignationId == designationId
                                    select new Designation
                                    {
                                        ID = d.Id,
                                        DesignationId = d.DesignationId,
                                        DesignationDesc = d.DesignationDesc
                                    }).ToList();

            
                if (designations == null || !designations.Any())
                {
                    return new VivifyResponse<List<Designation>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No designations found"
                    };
                }

                return new VivifyResponse<List<Designation>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = designations
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<Designation>>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }
        [HttpGet]
        [ActionName("GetBranchInfo")]
        public VivifyResponse<List<BranchInfo>> GetBranchInfo(string? BranchCode = null, int? siteId = null)
        {
            try
            {
                var branches = (from b in CommonDBContext.MBranches
                                join s in CommonDBContext.MSiteTypes
                                on b.SiteType equals s.Id into siteGroup
                                from site in siteGroup.DefaultIfEmpty()

                                where (string.IsNullOrEmpty(BranchCode) || b.BranchCode == BranchCode)  
                                && (!siteId.HasValue || b.SiteType == siteId)                       
                                && b.IsActive == true && (site == null || site.IsActive == true)    

                                select new BranchInfo
                                {
                                    Id = b.Id,                   
                                    BranchCode = b.BranchCode,    
                                    BranchName = b.BranchDesc,
                                    GeoFencing = b.Geofencing,  
                                    SiteId = b.SiteType,         
                                    SiteDesc = site != null ? site.SiteDescription : "N/A"  
                                }).ToList();

              
                if (!branches.Any())
                {
                    return new VivifyResponse<List<BranchInfo>>
                    {
                        StatusCode = 404,
                        StatusDesc = (BranchCode == null || siteId == null)
                            ? $"No branches found for the provided parameters."
                            : "No branches found.",
                        Result = new List<BranchInfo>()
                    };
                }

                return new VivifyResponse<List<BranchInfo>>
                {
                    StatusCode = 200,
                    StatusDesc = "Branches retrieved successfully.",
                    Result = branches
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<BranchInfo>>
                {
                    StatusCode = 500,
                    StatusDesc = "Error: " + ex.Message,
                    Result = new List<BranchInfo>()
                };
            }
        }
      [HttpGet]
[ActionName("GetLeaveTypes")]
public VivifyResponse<List<LeaveTypeDTO>> GetLeaveTypes()
{
    try
    {
        var leaveTypes = (from lt in HRMSDBContext.LeaveTypes
                         
                          select new LeaveTypeDTO
                          {
                              LeaveTypeId = lt.LeaveTypeId,
                              LeaveType = lt.LeaveType,
                          }).ToList();

        if (!leaveTypes.Any())
        {
            return new VivifyResponse<List<LeaveTypeDTO>>
            {
                StatusCode = 404,
                StatusDesc = "No leave types found",
                Result = new List<LeaveTypeDTO>()
            };
        }

        return new VivifyResponse<List<LeaveTypeDTO>>
        {
            StatusCode = 200,
            StatusDesc = "Loaded Successfully",
            Result = leaveTypes
        };
    }
    catch (Exception ex)
    {
        return new VivifyResponse<List<LeaveTypeDTO>>
        {
            StatusCode = 500,
            StatusDesc = $"Error: {ex.Message}",
            Result = new List<LeaveTypeDTO>()
        };
    }
}



        [HttpGet]
        [ActionName("GetAllAdvanceRequests")]
        public VivifyResponse<object> GetAllAdvanceRequests(
    [FromQuery] DateOnly? fromDate = null,
    [FromQuery] DateOnly? toDate = null,
    [FromQuery] long? empNo = null,
    [FromQuery] string? branchCode = null)
        {
            try
            {
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                Console.WriteLine($"Input Parameters: fromDate={fromDate}, toDate={toDate}, empNo={empNo}, branchCode={branchCode}, CompanyID={CompanyID}");

                var advancesQuery = from adv in HRMSDBContext.Advances
                                    join advType in HRMSDBContext.AdvanceTypes
                                    on adv.AdvanceTypeID equals advType.AdvanceTypeID into advGroup
                                    from advType in advGroup.DefaultIfEmpty()
                                    where adv.CompanyID == CompanyID
                                    && adv.Status == "0"
                                    select new
                                    {
                                        adv.EmpNo,
                                        adv.BranchCode,
                                        adv.AdvanceTypeID,
                                        AdvanceDesc = advType != null ? advType.AdvanceDesc : "N/A",
                                        adv.ReqAmnt,
                                        adv.Status,
                                        adv.ReqDate,
                                        adv.CreatedDate,
                                        adv.Remarks,
                                        adv.Attachment
                                    };

                if (fromDate.HasValue && toDate.HasValue)
                {
                    advancesQuery = advancesQuery
                        .Where(adv => adv.CreatedDate >= fromDate.Value.ToDateTime(TimeOnly.MinValue)
                                   && adv.CreatedDate <= toDate.Value.ToDateTime(TimeOnly.MaxValue));
                }

                if (empNo.HasValue)
                {
                    advancesQuery = advancesQuery.Where(adv => adv.EmpNo == empNo.Value);
                }

                if (!string.IsNullOrEmpty(branchCode))
                {
                    advancesQuery = advancesQuery.Where(adv => adv.BranchCode == branchCode);
                }

                var advances = advancesQuery.ToList();
                Console.WriteLine($"Fetched {advances.Count} advance requests.");

                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Where(e => advances.Select(a => a.EmpNo).Contains(e.EmpNo))
                    .ToList();

                Console.WriteLine($"Fetched {employeeInfos.Count} employee info records.");

                var virtualPath = Convert.ToString(Config["VirtualPath"]);

                var lstAdvanceRequests = advances
                    .Select(adv => new AdvanceRequest
                    {
                        EmpNo = adv.EmpNo,
                        EmpName = $"{adv.EmpNo} - {employeeInfos.FirstOrDefault(e => e.EmpNo == adv.EmpNo)?.FirstName ?? "Unknown"}",
                        BranchID = adv.BranchCode,
                        AdvanceTypeID = adv.AdvanceTypeID,
                        AdvanceDesc = adv.AdvanceDesc,
                        ReqAmnt = adv.ReqAmnt,
                        Status = adv.Status,
                        CreatedDate = adv.CreatedDate,
                        Remarks = adv.Remarks ?? string.Empty,
                        Attachment = !string.IsNullOrEmpty(adv.Attachment) ? $"{virtualPath}{adv.Attachment}" : string.Empty // Concatenate virtual path
                    })
                    .ToList();

                Console.WriteLine($"Mapped {lstAdvanceRequests.Count} advance requests.");

                if (!lstAdvanceRequests.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No records found.",
                        Result = new
                        {
                            message = "No records found."
                        }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = lstAdvanceRequests
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new
                    {
                        message = "An error occurred while processing your request."
                    }
                };
            }
        }

        [HttpGet]
        [ActionName("GetAdvanceApproval")]
        public VivifyResponse<object> GetAdvanceApproval(
     [FromQuery] DateOnly? fromDate = null,
     [FromQuery] DateOnly? toDate = null,
     [FromQuery] long? empNo = null,
     [FromQuery] string? branchCode = null,
     [FromQuery] string? status = null)
        {
            try
            {
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var advancesQuery = from adv in HRMSDBContext.Advances
                                    join advType in HRMSDBContext.AdvanceTypes
                                    on adv.AdvanceTypeID equals advType.AdvanceTypeID into advGroup
                                    from advType in advGroup.DefaultIfEmpty()
                                    where adv.CompanyID == CompanyID
                                    select new
                                    {
                                        adv.EmpNo,
                                        adv.BranchCode,
                                        adv.AdvanceTypeID,
                                        AdvanceDesc = advType != null ? advType.AdvanceDesc : "N/A",
                                        adv.ReqAmnt,
                                        adv.Status,
                                        adv.ApproveCmnt,
                                        adv.ReqDate,
                                        adv.CreatedDate,
                                        adv.Remarks,
                                        adv.Attachment
                                    };

               
                if (string.IsNullOrEmpty(status))
                {
                    advancesQuery = advancesQuery.Where(adv => adv.Status == "1" || adv.Status == "2");
                }
                else
                {
                    advancesQuery = advancesQuery.Where(adv => adv.Status == status);
                }

                if (fromDate.HasValue && toDate.HasValue)
                {
                    advancesQuery = advancesQuery
                        .Where(adv => adv.CreatedDate >= fromDate.Value.ToDateTime(TimeOnly.MinValue)
                                   && adv.CreatedDate <= toDate.Value.ToDateTime(TimeOnly.MaxValue));
                }

                if (empNo.HasValue)
                {
                    advancesQuery = advancesQuery.Where(adv => adv.EmpNo == empNo.Value);
                }

                if (!string.IsNullOrEmpty(branchCode))
                {
                    advancesQuery = advancesQuery.Where(adv => adv.BranchCode == branchCode);
                }

                var advances = advancesQuery.ToList();

                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Where(e => advances.Select(a => a.EmpNo).Contains(e.EmpNo))
                    .ToList();

                var lstAdvanceRequests = advances
                    .Select(adv => new AdvanceRequest
                    {
                        EmpNo = adv.EmpNo,
                        EmpName = $"{adv.EmpNo} - {employeeInfos.FirstOrDefault(e => e.EmpNo == adv.EmpNo)?.FirstName ?? "Unknown"}",
                        BranchID = adv.BranchCode,
                        AdvanceTypeID = adv.AdvanceTypeID,
                        AdvanceDesc = adv.AdvanceDesc,
                        ReqAmnt = adv.ReqAmnt,
                        Status = adv.Status,
                        CreatedDate=adv.CreatedDate,
                        Remarks = adv.Remarks ?? string.Empty,
                        Approvecmnt = adv.ApproveCmnt,
                        Attachment = adv.Attachment ?? string.Empty
                    })
                    .ToList();

                if (!lstAdvanceRequests.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No records found.",
                        Result = new { message = "No records found." }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = lstAdvanceRequests
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new { message = "An error occurred while processing your request." }
                };
            }
        }


        [HttpGet]
        [ActionName("GetAdvanceReport")]
        public VivifyResponse<List<AdvanceRequest>> GetAdvanceReport(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
        {
            try
            {
                int companyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                // Get EmpNo from JWT token claims
                var empNoClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value;

                if (!long.TryParse(empNoClaim, out long empNo))
                {
                    return new VivifyResponse<List<AdvanceRequest>>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing EmpNo claim.",
                        Result = new List<AdvanceRequest>()
                    };
                }

                var lstAdvanceRequests = (from adv in HRMSDBContext.Advances
                                          join advType in HRMSDBContext.AdvanceTypes
                                          on adv.AdvanceTypeID equals advType.AdvanceTypeID into advGroup
                                          from advType in advGroup.DefaultIfEmpty()

                                          where adv.CompanyID == companyID
                                          && adv.EmpNo == empNo   // filter for logged in employee
                                          && (!fromDate.HasValue || adv.CreatedDate >= fromDate)
                                          && (!toDate.HasValue || adv.CreatedDate <= toDate)

                                          select new AdvanceRequest
                                          {
                                              EmpNo = adv.EmpNo,
                                              BranchID = adv.BranchCode,
                                              AdvanceTypeID = adv.AdvanceTypeID,
                                              AdvanceDesc = advType != null ? advType.AdvanceDesc : " ",
                                              ReqAmnt = adv.ReqAmnt,
                                              ApprAmnt = adv.ApprAmnt ?? 0,
                                              Status = adv.Status,
                                              CreatedDate = adv.CreatedDate,
                                              Remarks = adv.Remarks ?? string.Empty,
                                              Approvecmnt = adv.ApproveCmnt ?? string.Empty,
                                              Attachment = adv.Attachment ?? string.Empty,
                                              PaymentAmount = adv.Payment ?? 0,
                                              PaymentDate = adv.PaymentDate
                                          })
                                          .ToList();

                if (!lstAdvanceRequests.Any())
                {
                    return new VivifyResponse<List<AdvanceRequest>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No advance requests found for the logged-in employee within the given date range.",
                        Result = new List<AdvanceRequest>()
                    };
                }

                return new VivifyResponse<List<AdvanceRequest>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = lstAdvanceRequests
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<AdvanceRequest>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new List<AdvanceRequest>()
                };
            }
        }


        [HttpGet]
        [ActionName("GetAdvanceRemarksDetails")]
        public VivifyResponse<object> GetAdvanceRemarksDetails([FromQuery] long empNo, [FromQuery] DateTime reqDate)
        {
            try
            {
                
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

              
                var record = HRMSDBContext.Advances
                    .FirstOrDefault(x => x.EmpNo == empNo
                                      && x.CompanyID == companyId
                                      && x.ReqDate == reqDate.Date
                                      && x.Status == "0");  

               
                if (record == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No advance request found for the specified employee, date, and status 0.",
                        Result = new
                        {
                            Message = "No records found."
                        }
                    };
                }

               
                var result = new
                {
                    EmpNo = record.EmpNo,
                    CompanyId = record.CompanyID,
                    BranchID = record.BranchCode,
                    AdvanceTypeID = record.AdvanceTypeID,
                    ReqDate = record.ReqDate,
                    ReqAmnt = record.ReqAmnt,
                    Remarks = record.Remarks ?? "No remarks available",  
                    Attachment = record.Attachment ?? "No attachment",  
                    Status = record.Status
                };

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Advance request details retrieved successfully.",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new
                    {
                        Message = "No records found."
                    }
                };
            }
        }

        [HttpPost]
        [ActionName("RejectAdvanceRequest")]
        public VivifyResponse<string> RejectAdvanceRequest([FromBody] RejectAdvanceRequestDto request)
        {
            try
            {
               
                long authenticatedEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int companyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

              
                var advanceRequest = HRMSDBContext.Advances
                    .FirstOrDefault(adv => adv.EmpNo == request.EmpNo
                                        && adv.CreatedDate.Date == request.CreatedDate.Date
                                        && adv.CompanyID == companyID);

                if (advanceRequest == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "Advance request not found."
                    };
                }

               
                advanceRequest.Status = "2"; 
                advanceRequest.ApproveCmnt = request.ApproveCmt;  
                advanceRequest.UpdatedDate = DateTime.Now;
                advanceRequest.UpdatedBy = authenticatedEmpNo;

              
                HRMSDBContext.SaveChanges();

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Advance request rejected.",
                    Result = "Success"
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}"
                };
            }
        }
        [HttpPost]
        [ActionName("UpdateAdvancePayment")]
        public VivifyResponse<object> UpdateAdvancePayment([FromBody] AdvancePaymentRequest request)
        {
            try
            {
                if (request == null || request.EmpNo <= 0 || request.CreatedDate == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid request data.",
                        Result = new { Message = "EmpNo and CreatedDate are required." }
                    };
                }

               
                string doneBy = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value;

                if (string.IsNullOrEmpty(doneBy))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 401,
                        StatusDesc = "Unauthorized",
                        Result = new { Message = "Unable to retrieve EmpNo from token." }
                    };
                }

                var advance = HRMSDBContext.Advances
                    .FirstOrDefault(a => a.EmpNo == request.EmpNo && a.CreatedDate.Date == request.CreatedDate.Date);

                if (advance == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Advance request not found.",
                        Result = new { Message = "No advance request found for the given EmpNo and CreatedDate." }
                    };
                }

                if (advance.Status != "1")
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Advance not approved for payment.",
                        Result = new { Message = "Payment cannot be made. Please make sure the advance amount is approved." }
                    };
                }

               
                advance.Payment = request.PaymentAmount;
                advance.PaymentStatus = "1";
                advance.PaymentDate = DateTime.Now;
                advance.DoneBy = doneBy;

                HRMSDBContext.SaveChanges();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Payment done successfully.",
                    Result = new { Message = "Advance payment recorded." }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { Message = "Error while updating payment." }
                };
            }
        }





        [HttpPost]
        [ActionName("UpdateAdvanceRequest")]
        public VivifyResponse<object> UpdateAdvanceRequest([FromBody] UpdateApprovalRequest requestData)
        {
            try
            {
                long employeeNumber = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

                if (requestData.ApprAmnt <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Approved amount must be greater than zero.",
                        Result = null
                    };
                }

                if (string.IsNullOrEmpty(requestData.ApproveCmt))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Approval comment cannot be empty.",
                        Result = null
                    };
                }

                var advanceRecord = HRMSDBContext.Advances
                    .FirstOrDefault(ar => ar.EmpNo == requestData.empNo &&
                                          ar.CreatedDate.Date == requestData.CreatedDate.Date &&
                                          ar.CompanyID == companyId);

                if (advanceRecord == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Advance request not found.",
                        Result = null
                    };
                }

               
                advanceRecord.Status = "1"; 
                advanceRecord.ApprAmnt = requestData.ApprAmnt;
                advanceRecord.ApproveCmnt = requestData.ApproveCmt;
                advanceRecord.UpdatedBy = employeeNumber;
                advanceRecord.UpdatedDate = DateTime.Now;
                advanceRecord.PaymentStatus = "0";

                HRMSDBContext.SaveChanges();
              
                var empInfo = CommonDBContext.EmployeeInfos
                    .FirstOrDefault(e => e.EmpNo == advanceRecord.EmpNo);

                string empDisplay = empInfo != null
                    ? $"{advanceRecord.EmpNo} - {empInfo.FirstName} {empInfo.LastName}".Trim()
                    : $"{advanceRecord.EmpNo} - Unknown";

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Advance request approved successfully.",
                    Result = new
                    {
                        Emp = empDisplay,
                        CreatedDate = advanceRecord.CreatedDate.ToString("dd-MM-yyyy"),
                        ApprovedAmount = advanceRecord.ApprAmnt
                    }
                };
            }
            catch (Exception exception)
            {
                var innerExceptionMessage = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {exception.Message}. Inner Exception: {innerExceptionMessage}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetAdvancePayments")]
        public VivifyResponse<object> GetAdvancePayments(
         [FromQuery] DateOnly? fromDate = null,
         [FromQuery] DateOnly? toDate = null,
         [FromQuery] long? empNo = null,
         [FromQuery] string? branchCode = null,
         [FromQuery] string? paymentStatus = null)
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var branches = CommonDBContext.MBranches.ToDictionary(b => b.BranchCode, b => b.BranchDesc);

                var paymentsQuery = from adv in HRMSDBContext.Advances
                                    join advType in HRMSDBContext.AdvanceTypes
                                        on adv.AdvanceTypeID equals advType.AdvanceTypeID into advGroup
                                    from advType in advGroup.DefaultIfEmpty()
                                    where adv.CompanyID == companyId
                                    select new
                                    {
                                        adv.EmpNo,
                                        adv.BranchCode,
                                        BranchName = branches.ContainsKey(adv.BranchCode) ? branches[adv.BranchCode] : "N/A",
                                        adv.AdvanceTypeID,
                                        AdvanceDesc = advType.AdvanceDesc,
                                        adv.ReqAmnt,
                                        adv.Payment,
                                        adv.PaymentStatus,
                                        adv.PaymentDate,
                                        adv.ApprAmnt,
                                        adv.DoneBy,
                                        adv.ReqDate,
                                        adv.CreatedDate
                                    };

                // Payment status filtering - corrected section
                if (!string.IsNullOrWhiteSpace(paymentStatus))
                {
                    string trimmedStatus = paymentStatus.Trim();
                    if (trimmedStatus == "0" || trimmedStatus == "1")
                    {
                        paymentsQuery = paymentsQuery.Where(p =>
                            p.PaymentStatus != null && p.PaymentStatus.Trim() == trimmedStatus);
                    }
                    else
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 400,
                            StatusDesc = "Invalid Payment Status",
                            Result = new { message = "Only '0' (Unpaid) or '1' (Paid) are valid payment status values." }
                        };
                    }
                }
                else
                {
                    // If no status filter is provided, show both paid and unpaid by default
                    paymentsQuery = paymentsQuery.Where(p =>
                        p.PaymentStatus == "0" || p.PaymentStatus == "1");
                }

                if (fromDate.HasValue)
                {
                    paymentsQuery = paymentsQuery.Where(p => p.PaymentDate >= fromDate.Value.ToDateTime(TimeOnly.MinValue));
                }

                if (toDate.HasValue)
                {
                    paymentsQuery = paymentsQuery.Where(p => p.PaymentDate <= toDate.Value.ToDateTime(TimeOnly.MaxValue));
                }

                if (empNo.HasValue)
                {
                    paymentsQuery = paymentsQuery.Where(p => p.EmpNo == empNo.Value);
                }

                if (!string.IsNullOrEmpty(branchCode))
                {
                    paymentsQuery = paymentsQuery.Where(p => p.BranchCode == branchCode);
                }

                var paymentList = paymentsQuery.ToList();

                var empNosToResolve = paymentList.Select(p => p.EmpNo)
                    .Union(paymentList.Where(p => p.DoneBy != null).Select(p => Convert.ToInt64(p.DoneBy)))
                    .Distinct()
                    .ToList();

                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Where(e => empNosToResolve.Contains(e.EmpNo))
                    .ToList();

                var resultList = paymentList.Select(p => new
                {
                    p.EmpNo,
                    EmpName = $"{p.EmpNo} - {employeeInfos.FirstOrDefault(e => e.EmpNo == p.EmpNo)?.FirstName ?? "Unknown"}",
                    p.BranchCode,
                    BranchName = p.BranchName,
                    p.AdvanceTypeID,
                    AdvanceDesc = p.AdvanceDesc,
                    RequestedAmount = p.ReqAmnt,
                    PaymentAmount = p.Payment,
                    ApprovedAmount = p.ApprAmnt,
                    PaymentStatus = p.PaymentStatus,
                    PaymentStatusText = p.PaymentStatus == "1" ? "Paid" : "Unpaid",
                    PaymentDate = p.PaymentDate,
                    DoneBy = p.DoneBy,
                    DoneByName = !string.IsNullOrEmpty(p.DoneBy)
                                ? employeeInfos.FirstOrDefault(e => e.EmpNo.ToString() == p.DoneBy)?.FirstName ?? "Unknown"
                                : null,
                    p.ReqDate,
                    p.CreatedDate
                }).ToList();

                if (!resultList.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No payment records found.",
                        Result = new { message = "No payments found for the given filters." }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = resultList
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new { message = "An error occurred while processing your request." }
                };
            }
        }
        [HttpPost]
        [ActionName("UpdateLeaveRequest")]
        public VivifyResponse<decimal> UpdateLeaveRequest([FromBody] UpdateLeaveApprovalRequest requestData)
        {
            using var transaction = HRMSDBContext.Database.BeginTransaction();
            try
            {
                long approverEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

                
                var leaveRecord = HRMSDBContext.LeaveRequests
                    .FirstOrDefault(lr => lr.EmpNo == requestData.empNo &&
                                        lr.LeaveTypeId == requestData.LeaveTypeID &&
                                        lr.Crt_Date.Date == requestData.CreatedDate.Date &&
                                        lr.CompanyId == companyId);

                if (leaveRecord == null)
                {
                    return new VivifyResponse<decimal>
                    {
                        StatusCode = 404,
                        StatusDesc = "Leave request not found.",
                        Result = 0
                    };
                }

              
                var leaveType = HRMSDBContext.LeaveTypes
                    .FirstOrDefault(lt => lt.LeaveTypeId == requestData.LeaveTypeID && lt.CompanyId == companyId);

                if (leaveType == null)
                {
                    return new VivifyResponse<decimal>
                    {
                        StatusCode = 404,
                        StatusDesc = "Leave type not found.",
                        Result = 0
                    };
                }

               
                decimal totalDeductions = HRMSDBContext.LeaveBalances
                    .Where(lb => lb.EmpNo == requestData.empNo
                              && lb.LeaveTypeId == requestData.LeaveTypeID
                              && lb.CompanyId == companyId)
                    .Sum(lb => (decimal?)lb.NoOfLeaveDays) ?? 0;

                decimal remainingBalance = leaveType.NoOfDays + totalDeductions;

              
                var leaveBalance = new LeaveBalance
                {
                    CompanyId = companyId,
                    BranchId = leaveRecord.BranchId,
                    EmpNo = requestData.empNo,
                    LeaveTypeId = requestData.LeaveTypeID,
                    NoOfLeaveDays = -Math.Abs(requestData.ApprovedDays), 
                    FromDate = leaveRecord.FromDate,
                    ToDate = leaveRecord.ToDate,
                    Expired = false,
                    CreatedBy = approverEmpNo.ToString(),
                    CreatedDate = DateTime.Now,
                    UpdateDate = DateTime.Now,
                    UpdatedBy = approverEmpNo.ToString(),
                    LeaveDate = DateTime.Now 
                };
                HRMSDBContext.LeaveBalances.Add(leaveBalance);

               
                leaveRecord.Status = "1";
                leaveRecord.NoOfDays = requestData.ApprovedDays;
                leaveRecord.ApproverComment = requestData.ApproveCmt;
                leaveRecord.Upt_By = approverEmpNo.ToString();
                leaveRecord.Upt_Date = DateTime.Now;

                HRMSDBContext.SaveChanges();
                transaction.Commit();

                decimal newBalance = remainingBalance - requestData.ApprovedDays;
                return new VivifyResponse<decimal>
                {
                    StatusCode = 200,
                    StatusDesc = $"{requestData.ApprovedDays} days approved. New balance: {newBalance}",
                    Result = newBalance
                };
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return new VivifyResponse<decimal>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = 0
                };
            }
        }


        [HttpPost]
        [ActionName("RejectLeaveRequest")]
        public VivifyResponse<string> RejectLeaveRequest([FromBody] RejectLeaveRequestDto request)
        {
            try
            {
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var leaveRequest = HRMSDBContext.LeaveRequests
                    .FirstOrDefault(lr => lr.EmpNo == request.EmpNo
                                       && lr.ReqDate.Date == request.ReqDate.Date
                                       && lr.CompanyId == companyId);

                if (leaveRequest == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "Leave request not found."
                    };
                }

                leaveRequest.Status = "2";
                leaveRequest.ApproverComment = request.ApproverComment; 
                leaveRequest.Upt_Date = DateTime.Now;
                leaveRequest.Upt_By = empNo.ToString();

                HRMSDBContext.SaveChanges();

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Leave request rejected successfully.",
                    Result = "Success"
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}"
                };
            }
        }
        [HttpGet]
        [ActionName("GetLeaveRequestDetails")]
        public VivifyResponse<object> GetLeaveRequestDetails([FromQuery] long? empNo, [FromQuery] DateTime? fromDate)
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Select(e => new { e.EmpNo, e.FirstName })
                    .ToList(); 

                var records = (from lr in HRMSDBContext.LeaveRequests
                               join lt in HRMSDBContext.LeaveTypes on lr.LeaveTypeId equals lt.LeaveTypeId
                               where lr.CompanyId == companyId && lr.Status == "0"
                               select new
                               {
                                   lr.EmpNo,
                                   lr.CompanyId,
                                   lr.BranchId,
                                   lr.LeaveTypeId,
                                   LeaveTypeName = lt.LeaveType,
                                   lr.ReqDate,
                                   lr.FromDate,
                                   lr.ToDate,
                                   lr.NoOfDays,
                                   lr.Remarks,
                                   lr.Status,
                                   lr.Attachment 
                               })
                             .AsEnumerable()
                             .Select(lr => new
                             {
                                 lr.EmpNo,
                                 EmpName = $"{lr.EmpNo} - {employeeInfos.FirstOrDefault(e => e.EmpNo == lr.EmpNo)?.FirstName ?? "Unknown"}",
                                 lr.CompanyId,
                                 lr.BranchId,
                                 lr.LeaveTypeId,
                                 lr.LeaveTypeName,
                                 lr.ReqDate,
                                 lr.FromDate,
                                 lr.ToDate,
                                 lr.NoOfDays,
                                 Remarks = lr.Remarks ?? "No remarks available",
                                 lr.Status,
                                 Attachment = lr.Attachment ?? "No attachment"
                             })
                             .Where(x => (!empNo.HasValue || x.EmpNo == empNo.Value)
                                  && (!fromDate.HasValue || x.ReqDate.Date == fromDate.Value.Date))
                             .ToList();

                if (records == null || records.Count == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No leave requests found for the given criteria.",
                        Result = new { Message = "No records found." }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Leave request details retrieved successfully.",
                    Result = records
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new { Message = "No records found due to an error." }
                };
            }
        }


        [HttpGet]
        [ActionName("GetLeaveRequests")]
        public VivifyResponse<List<LeaveRequestViewDTO>> GetLeaveRequests(DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

                var leaveRequests = (from lr in HRMSDBContext.LeaveRequests
                                     join lt in HRMSDBContext.LeaveTypes
                                     on lr.LeaveTypeId equals lt.LeaveTypeId into leaveTypeJoin
                                     from lt in leaveTypeJoin.DefaultIfEmpty()

                                     join lb in HRMSDBContext.LeaveBalances
                                     on new { lr.EmpNo, lr.CompanyId, lr.LeaveTypeId } equals new { lb.EmpNo, lb.CompanyId, lb.LeaveTypeId } into leaveBalanceJoin
                                     from lb in leaveBalanceJoin.DefaultIfEmpty()

                                     where lr.EmpNo == empNo
                                           && lr.CompanyId == companyId
                                           && lr.IsActive
                                           && (!fromDate.HasValue || lr.ReqDate.Date >= fromDate.Value.Date)
                                           && (!toDate.HasValue || lr.ReqDate.Date <= toDate.Value.Date)
                                     orderby lr.Id descending
                                     select new LeaveRequestViewDTO
                                     {
                                         LeaveTypeId = lr.LeaveTypeId,
                                         LeaveTypeName = lt.LeaveType,
                                         FromDate = lr.FromDate,
                                         ToDate = lr.ToDate,
                                         ReqDate = lr.ReqDate,
                                         Remarks = lr.Remarks ?? "",
                                         NoOfDays = lr.NoOfDays,
                                         Status = lr.Status ?? "",
                                         ApproverComment = lr.ApproverComment ?? "",
                                         LeaveBalanceDays = lb != null ? Math.Abs(lb.NoOfLeaveDays) : 0
                                     }).Distinct().ToList();


                return new VivifyResponse<List<LeaveRequestViewDTO>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = leaveRequests
                };
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<LeaveRequestViewDTO>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerMessage}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddLeaveRequest")]
        public async Task<VivifyResponse<bool>> AddLeaveRequest([FromForm] LeaveRequestDTO requestData)
        {
            try
            {
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

                var employeeInfo = CommonDBContext.EmployeeInfos
                    .Where(e => e.EmpNo == empNo && e.CompanyId == companyId)
                    .Select(e => new { e.BranchCode })
                    .FirstOrDefault();

                if (employeeInfo == null)
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found or invalid credentials.",
                        Result = false
                    };
                }

                string branchCode = employeeInfo.BranchCode;

                if (string.IsNullOrEmpty(branchCode))
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 400,
                        StatusDesc = "Branch code not available for the employee.",
                        Result = false
                    };
                }

               
                string attachmentPath = null;
                if (requestData.AttachmentFile != null)
                {
                    attachmentPath = await SaveAttachmentFileAsync(requestData.AttachmentFile);
                }

                var leaveRequest = new LeaveRequests
                {
                    CompanyId = companyId,
                    EmpNo = empNo,
                    BranchId = branchCode,
                    LeaveTypeId = requestData.LeaveTypeId,
                    FromDate = requestData.FromDate,
                    ToDate = requestData.ToDate,
                    ReqDate = DateTime.Now,
                    Remarks = requestData.Remarks,
                    NoOfDays = requestData.NoOfDays,
                    Status = "0",
                    IsActive = true,
                    Crt_By = empNo.ToString(),
                    Crt_Date = DateTime.Now,
                    Upt_Date = null,
                    Attachment = attachmentPath 
                };

                HRMSDBContext.LeaveRequests.Add(leaveRequest);
                await HRMSDBContext.SaveChangesAsync();

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = "Leave request submitted successfully.",
                    Result = true
                };
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerMessage}",
                    Result = false
                };
            }
        }

        [HttpGet]
        [ActionName("GetLeaveReqData")]
        public VivifyResponse<List<LeaveRequestViewDTO>> GetLeaveReqData(
     DateTime? fromDate = null,
     DateTime? toDate = null,
     long? empNo = null,
     string branchCode = null) 
        {
            try
            {
               
                long loggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

               
                long requestedEmpNo = empNo ?? loggedInEmpNo;

              
                Console.WriteLine($"Input Parameters: fromDate={fromDate}, toDate={toDate}, branchCode={branchCode}, empNo={empNo}");
                Console.WriteLine($"LoggedInEmpNo: {loggedInEmpNo}, CompanyID: {companyId}");

               
                var leaveRequestsQuery = HRMSDBContext.LeaveRequests
                    .Where(lr => lr.CompanyId == companyId
                                 && lr.IsActive
                                 && (!empNo.HasValue || lr.EmpNo == requestedEmpNo)
                                 && (!fromDate.HasValue || lr.ReqDate.Date >= fromDate.Value.Date)
                                 && (!toDate.HasValue || lr.ReqDate.Date <= toDate.Value.Date)
                                 && (string.IsNullOrEmpty(branchCode) || lr.BranchId == branchCode)
                                 && lr.Status == "0"); // Default to status "0"

                var leaveRequests = leaveRequestsQuery.ToList();

               
                Console.WriteLine($"Fetched {leaveRequests.Count} leave requests from HRMSDBContext.");

               
                var leaveTypes = HRMSDBContext.LeaveTypes.ToList();
                Console.WriteLine($"Fetched {leaveTypes.Count} leave types from HRMSDBContext.");

                var employeeInfos = CommonDBContext.EmployeeInfos.ToList();
                Console.WriteLine($"Fetched {employeeInfos.Count} employee infos from CommonDBContext.");

                var mBranches = CommonDBContext.MBranches.ToList();
                Console.WriteLine($"Fetched {mBranches.Count} branches from CommonDBContext.");

               
                var result = (from lr in leaveRequests
                              join lt in leaveTypes
                                  on lr.LeaveTypeId equals lt.LeaveTypeId into leaveTypeJoin
                              from lt in leaveTypeJoin.DefaultIfEmpty()
                              join ei in employeeInfos
                                  on lr.EmpNo equals ei.EmpNo into employeeJoin
                              from ei in employeeJoin.DefaultIfEmpty()
                              join mb in mBranches
                                  on lr.BranchId equals mb.BranchCode into branchJoin
                              from mb in branchJoin.DefaultIfEmpty()
                              orderby lr.Id descending
                              select new LeaveRequestViewDTO
                              {
                                  EmpNo = lr.EmpNo,
                                  EmpName = $"{lr.EmpNo} - {ei?.FirstName ?? "Unknown"}", 
                                  BranchId = lr.BranchId,
                                  BranchName = mb?.BranchDesc ?? "Unknown", 
                                  LeaveTypeId = lr.LeaveTypeId,
                                  LeaveTypeName = lt?.LeaveType ?? "Unknown", 
                                  FromDate = lr.FromDate,
                                  ToDate = lr.ToDate,
                                  ReqDate = lr.ReqDate,
                                  Crt_Date = lr.Crt_Date,
                                  Remarks = lr.Remarks ?? "No remarks",
                                  NoOfDays = lr.NoOfDays,
                                  Status = lr.Status ?? "Pending",
                                  ApproverComment = lr.ApproverComment
                              }).ToList();

               
                Console.WriteLine($"Final result contains {result.Count} records.");

                return new VivifyResponse<List<LeaveRequestViewDTO>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<LeaveRequestViewDTO>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerMessage}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetAllLeaveStatusReport")]
        public VivifyResponse<List<LeaveRequestViewDTO>> GetAllLeaveStatusReport(
    DateTime? fromDate = null,
    DateTime? toDate = null,
    long? empNo = null,
    string branchCode = null,
    string status = null)
        {
            try
            {
               
                long loggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

               
                long requestedEmpNo = empNo ?? loggedInEmpNo;

                Console.WriteLine($"Input Parameters: fromDate={fromDate}, toDate={toDate}, branchCode={branchCode}, empNo={empNo}, status={status}");
                Console.WriteLine($"LoggedInEmpNo: {loggedInEmpNo}, CompanyID: {companyId}");

               
                var leaveRequestsQuery = HRMSDBContext.LeaveRequests
                    .Where(lr => lr.CompanyId == companyId
                                 && lr.IsActive
                                 && (!empNo.HasValue || lr.EmpNo == requestedEmpNo)
                                 && (!fromDate.HasValue || lr.ReqDate.Date >= fromDate.Value.Date)
                                 && (!toDate.HasValue || lr.ReqDate.Date <= toDate.Value.Date)
                                 && (string.IsNullOrEmpty(branchCode) || lr.BranchId == branchCode));

              
                if (!string.IsNullOrEmpty(status))
                {
                    if (status == "1" || status == "2")
                    {
                        leaveRequestsQuery = leaveRequestsQuery.Where(lr => lr.Status == status);
                    }
                    else
                    {
                        return new VivifyResponse<List<LeaveRequestViewDTO>>
                        {
                            StatusCode = 400,
                            StatusDesc = "Invalid status parameter. Valid values are '1', or '2'.",
                            Result = null
                        };
                    }
                }
                else
                {
                    leaveRequestsQuery = leaveRequestsQuery.Where(lr => lr.Status == "1" || lr.Status == "2");
                }

                var leaveRequests = leaveRequestsQuery.ToList();
                Console.WriteLine($"Fetched {leaveRequests.Count} leave requests from HRMSDBContext.");

                var leaveTypes = HRMSDBContext.LeaveTypes.ToList();
                Console.WriteLine($"Fetched {leaveTypes.Count} leave types from HRMSDBContext.");

                var employeeInfos = CommonDBContext.EmployeeInfos.ToList();
                Console.WriteLine($"Fetched {employeeInfos.Count} employee infos from CommonDBContext.");

                var mBranches = CommonDBContext.MBranches.ToList();
                Console.WriteLine($"Fetched {mBranches.Count} branches from CommonDBContext.");

                var result = (from lr in leaveRequests
                              join lt in leaveTypes
                                  on lr.LeaveTypeId equals lt.LeaveTypeId into leaveTypeJoin
                              from lt in leaveTypeJoin.DefaultIfEmpty()
                              join ei in employeeInfos
                                  on lr.EmpNo equals ei.EmpNo into employeeJoin
                              from ei in employeeJoin.DefaultIfEmpty()
                              join mb in mBranches
                                  on lr.BranchId equals mb.BranchCode into branchJoin
                              from mb in branchJoin.DefaultIfEmpty()
                              orderby lr.Id descending
                              select new LeaveRequestViewDTO
                              {
                                  EmpNo = lr.EmpNo,
                                  EmpName = $"{lr.EmpNo} - {ei?.FirstName ?? "Unknown"}", 
                                  BranchId = lr.BranchId,
                                  BranchName = mb?.BranchDesc ?? "Unknown",
                                  LeaveTypeId = lr.LeaveTypeId,
                                  LeaveTypeName = lt?.LeaveType ?? "Unknown",
                                  FromDate = lr.FromDate,
                                  ToDate = lr.ToDate,
                                  ReqDate = lr.ReqDate,
                                  Crt_Date = lr.Crt_Date,
                                  Remarks = lr.Remarks , 
                                  NoOfDays = lr.NoOfDays,
                                  Status = lr.Status ?? "Pending",
                                  ApproverComment = lr.ApproverComment ,
                              }).ToList();

                Console.WriteLine($"Final result contains {result.Count} records.");

                return new VivifyResponse<List<LeaveRequestViewDTO>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<LeaveRequestViewDTO>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerMessage}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("AddAdvanceRequest")]
        public async Task<VivifyResponse<bool>> AddAdvanceRequest([FromForm] ApprovalRequest request)
        {
            try
            {
              
                long EmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var employee = CommonDBContext.EmployeeInfos.FirstOrDefault(e => e.EmpNo == EmpNo);

                if (employee == null)
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = false
                    };
                }

                string BranchCode = employee.BranchCode;

               
                string attachmentPath = null;

                if (request.AttachmentFile != null)
                {
                    attachmentPath = await SaveAttachmentFileAsync(request.AttachmentFile);
                }

              
                var newRequest = new Advance
                {
                    EmpNo = EmpNo,
                    CompanyID = CompanyID,
                    BranchCode = BranchCode,
                    AdvanceTypeID = request.AdvanceTypeID,
                    ReqAmnt = request.ReqAmt,
                    ReqDate = request.ReqDate, 
                    ToDate = request.ToDate,   
                    Remarks = request.Remarks,
                    Attachment = attachmentPath,
                    Status = "0",
                    CreatedDate = DateTime.Now,
                    CreatedBy = EmpNo
                };

               
                HRMSDBContext.Advances.Add(newRequest);
                await HRMSDBContext.SaveChangesAsync();

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = "Advance request successfully submitted.",
                    Result = true
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = false
                };
            }
        }
        [HttpPost]
        [ActionName("AdvanceApprovalRequest")]
        public async Task<VivifyResponse<bool>> AdvanceApprovalRequest([FromForm] ApprovalRequest request)
        {
            try
            {
               
                long EmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var employee = CommonDBContext.EmployeeInfos.FirstOrDefault(e => e.EmpNo == EmpNo);

                if (employee == null)
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = false
                    };
                }

                string BranchCode = employee.BranchCode;

                string attachmentPath = null;

                if (request.AttachmentFile != null) 
                {
                    attachmentPath = await SaveAttachmentFileAsync(request.AttachmentFile);
                }

                var newRequest = new Advance
                {
                    EmpNo = EmpNo,
                    CompanyID = CompanyID,
                    BranchCode = BranchCode,
                    AdvanceTypeID = request.AdvanceTypeID,
                    ReqAmnt = request.ReqAmt,
                    ReqDate = request.ReqDate,
                    Remarks = request.Remarks,
                    Attachment = attachmentPath, 
                    Status = "0",
                    CreatedDate = DateTime.Now,
                    CreatedBy = EmpNo
                };

               
                HRMSDBContext.Advances.Add(newRequest);
                await HRMSDBContext.SaveChangesAsync();

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = "Approval request successfully submitted.",
                    Result = true
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = false
                };
            }
        }
        private async Task<string> SaveAttachmentFileAsync(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    throw new ArgumentException("File cannot be null or empty.");

               
                var root = Convert.ToString(Config["VideoFilePath"]);
                var virtualPath = Convert.ToString(Config["VideoFileVirtualPath"]);

                if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(virtualPath))
                    throw new InvalidOperationException("File upload paths are not configured properly.");

            
                var extension = Path.GetExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(extension))
                    throw new ArgumentException("Invalid file extension.");

                
                var fileName = Guid.NewGuid().ToString() + extension;

               
                var Year = DateTime.Now.Year.ToString();
                root = Path.Combine(root, Year);

             
                var filePath = Path.Combine(root, fileName);

               
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

             
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

              
                var normalizedVirtualPath = $"{virtualPath.TrimEnd('/')}/{fileName}";
                return normalizedVirtualPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving file: {ex.Message}");
                throw;
            }
        }
        [HttpGet]
        [ActionName("GetAllBiometricExceptions")]
        public VivifyResponse<List<BiometricExceptionDetails>> GetAllBiometricExceptions(long? empNo = null, string branchCode = null)
        {
            try
            {
                long LoggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

               
                var biometricExceptions = HRMSDBContext.EmpBioExceptions.ToList();
                var employees = CommonDBContext.EmployeeInfos.ToList();
                var branches = CommonDBContext.MBranches.ToList();
                var designations = CommonDBContext.MDesignations.ToList();

                
                var lstExceptions = (from be in biometricExceptions
                                     join ei in employees on be.EmpNo equals ei.EmpNo
                                     join b in branches on ei.BranchCode equals b.BranchCode
                                     join d in designations on ei.Designation equals d.DesignationId
                                     where
                                         (empNo == null || ei.EmpNo == empNo) &&
                                         (string.IsNullOrEmpty(branchCode) || ei.BranchCode == branchCode)
                                     select new BiometricExceptionDetails
                                     {
                                         EmpNo = ei.EmpNo,
                                         FirstName = $"{ei.EmpNo} - {ei.FirstName}",
                                         LastName = ei.LastName,
                                         DesignationDesc = d.DesignationDesc,
                                         BranchDesc = b.BranchDesc,
                                         IsEligOutSidePunch = be.IsEligOutSidePunch,
                                         StartDate = be.StartDate, 
                                         EndDate = be.EndDate
                                     }).ToList();

                return new VivifyResponse<List<BiometricExceptionDetails>>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = lstExceptions
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<BiometricExceptionDetails>>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace

                };
            }
        }


        [HttpGet]
        [ActionName("GetUpdateEmployeeInfo")]
        public VivifyResponse<List<EmployeeDetailsUpdate>> GetEmployeeInfo(long? EmpNo = null)
        {
            try
            {
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var employees = (from e in CommonDBContext.EmployeeInfos
                                     // Join for Designation
                                 join d in CommonDBContext.MDesignations
                                 on e.Designation equals d.DesignationId into desigGroup
                                 from desig in desigGroup.DefaultIfEmpty()

                                     // Join for Branch
                                 join b in CommonDBContext.MBranches
                                 on e.BranchCode equals b.BranchCode into branchGroup
                                 from branch in branchGroup.DefaultIfEmpty()

                                     // Join for Role
                                 join r in CommonDBContext.MUserRoles
                                 on e.RoleId equals r.RoleId into roleGroup
                                 from role in roleGroup.DefaultIfEmpty()

                                     // Self-Join to get Line Manager's name based on ReportingEmpNo
                                 join lm in CommonDBContext.EmployeeInfos
                                 on e.ReportingEmpNo equals lm.EmpNo into lineManagerGroup
                                 from lineManager in lineManagerGroup.DefaultIfEmpty()

                                     // Join for Region
                                 join rg in CommonDBContext.MRegions
                                 on e.RegionId equals rg.RegionID into regionGroup
                                 from region in regionGroup.DefaultIfEmpty()

                                 where e.CompanyId == CompanyID
                                       && (EmpNo == null || e.EmpNo == EmpNo)
                                       && e.IsActive == true

                                 select new EmployeeDetailsUpdate
                                 {
                                     CompanyId = e.CompanyId.GetValueOrDefault(),
                                     EmpNo = e.EmpNo,
                                     FirstName = e.FirstName,
                                     LastName = e.LastName,
                                     EmailId = e.EmailId,
                                     Pwd = e.Pwd,
                                     Designation = e.Designation,
                                     DesignationDesc = desig != null ? desig.DesignationDesc : null,
                                     BranchCode = e.BranchCode,
                                     BranchDesc = branch != null ? branch.BranchDesc : null,
                                     PermAddress = e.PermAddress,
                                     ContAddress = e.ContAddress,
                                     ContactNumber = e.ContactNumber,
                                     EmergencyNumber = e.EmergencyNumber,
                                     ReportingEmpNo = e.ReportingEmpNo,

                                     // Get Line Manager's full name
                                     LineManager = lineManager != null ? $"{lineManager.FirstName} {lineManager.LastName}" : null,

                                     RoleName = role != null ? role.RoleName : null,
                                     RoleId = e.RoleId,
                                     BuildingNo = e.BuildingNo,
                                     Street = e.Street,
                                     City = e.City,
                                     Pincode = e.Pincode,
                                     Country = e.Country ?? "India",
                                     PersonalMail = e.PersonalMail,
                                     BankNo = e.BankNo,
                                     BankName = e.BankName,
                                     IFSCCode = e.IFSCCode,
                                     PhotoPath = e.PhotoPath,
                                     Dob = e.Dob.HasValue ? e.Dob.Value.ToDateTime(new TimeOnly(0, 0)) : (DateTime?)null,
                                     Gender = e.Gender,
                                     BloodGroup = e.BloodGroup,
                                     ContBuildingNo = e.ContBuildingNo,
                                     ContStreet = e.ContStreet,
                                     ContCity = e.ContCity,
                                     ContPinCode = e.ContPinCode,
                                     ContCountry = e.ContCountry ?? "India",
                                     DateOfJoin = e.DateOfJoin,
                                     NoticePeriod = e.NoticePeriod,
                                     IsActive = e.IsActive,
                                     RegionId = e.RegionId,
                                     RegionDesc = region != null ? region.RegionDesc : null,
                                     BankPassBook = e.BankPassBook,
                                     UAN_No = e.UAN_No,
                                 })
                                .ToList();

                if (employees == null || employees.Count == 0)
                {
                    return new VivifyResponse<List<EmployeeDetailsUpdate>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active employees found."
                    };
                }

                return new VivifyResponse<List<EmployeeDetailsUpdate>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = employees
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<EmployeeDetailsUpdate>>
                {
                    StatusCode = 500,
                    StatusDesc = $"{ex.Message}: {ex.StackTrace}"
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmployeeByEmpNo")]
        public VivifyResponse<List<EmployeeDetails>> GetEmployeeByEmpNo(long? EmpNo = null)
        {
            try
            {
                
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var employees = (from e in CommonDBContext.EmployeeInfos
                                 join d in CommonDBContext.MDesignations
                                 on e.Designation equals d.DesignationId into desigGroup
                                 from desig in desigGroup.DefaultIfEmpty()

                                 join b in CommonDBContext.MBranches
                                 on e.BranchCode equals b.BranchCode into branchGroup
                                 from branch in branchGroup.DefaultIfEmpty()

                                 where e.CompanyId == CompanyID
                                 && (EmpNo == null || e.EmpNo == EmpNo) 
                                 && e.IsActive == true
                                 select new EmployeeDetails
                                 {
                                     EmpNo = e.EmpNo,
                                     FirstName = e.FirstName,
                                     LastName = e.LastName,
                                     DesignationDesc = desig.DesignationDesc,
                                     BranchDesc = branch.BranchDesc
                                 })
                                 .ToList();

               
                if (employees == null || employees.Count == 0)
                {
                    return new VivifyResponse<List<EmployeeDetails>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active employees found."
                    };
                }

                return new VivifyResponse<List<EmployeeDetails>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = employees
                };
            }
            catch (Exception ex)
            {
               
                return new VivifyResponse<List<EmployeeDetails>>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmployee")]
        public async Task<VivifyResponse<dynamic>> AddEmployee([FromForm] EmployeeInfos request)
        {
            try
            {
               
                long EmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

              
                var existingEmployee = CommonDBContext.EmployeeInfos
                    .AsNoTracking()
                    .FirstOrDefault(x => x.CompanyId == CompanyID &&
                                        (x.EmailId == request.Email || x.EmpNo == request.EmpNo));

                if (existingEmployee != null)
                {
                    return new VivifyResponse<dynamic>
                    {
                        StatusCode = 409,
                        StatusDesc = "Employee with this email or employee number already exists.",
                        Result = null
                    };
                }

             
                string photoPath = null;

                if (request.PhotoPath != null) 
                {
                    photoPath = await SaveAttachmentFileAsync(request.PhotoPath); 
                }
               
                string Bankpassbook = null;

                if (request.BankPassBook != null) 
                {
                    Bankpassbook = await SaveAttachmentFileAsync(request.BankPassBook); 
                }

              
                var newEmployee = new SafetyToolBoxDL.CommonDB.EmployeeInfo
                {
                    CompanyId = CompanyID,
                    EmpNo = request.EmpNo,
                    FirstName = request.EmpName,
                    LastName = request.LastName,
                    EmailId = request.Email,
                    Pwd = request.Pwd,
                    Designation = request.Designation,
                    BranchCode = request.Branch,
                    ContactNumber = request.ContactNumber,
                    EmergencyNumber = string.IsNullOrWhiteSpace(request.EmergencyNumber) ? null : request.EmergencyNumber,
                    PhotoPath = photoPath, 
                    Dob = request.DateOfBirth.HasValue ? DateOnly.FromDateTime(request.DateOfBirth.Value) : null,
                    Gender = request.Gender,
                    IsActive = true,
                    CrtDate = DateTime.UtcNow,
                    CrtBy = EmpNo,
                    UptDate = DateTime.UtcNow,
                    UptBy = EmpNo,
                    ReportingEmpNo = request.ReportingEmpNo,
                    BuildingNo = request.BuildingNo,
                    Street = request.Street,
                    City = request.City,
                    Pincode = request.Pincode,
                    Country = string.IsNullOrWhiteSpace(request.Country) ? "India" : request.Country,
                    PersonalMail = request.PersonalMail,
                    BankNo = request.BankNo,
                    BankName = request.BankName,
                    IFSCCode = request.IFSCCode,
                    ContBuildingNo = request.ContBuildingNo,
                    ContStreet = request.ContStreet,
                    ContCity = request.ContCity,
                    ContPinCode = request.ContPinCode,
                    ContCountry = request.ContCountry,
                    DateOfJoin = request.DateOfJoin,
                    RoleId = request.RoleId,
                    BloodGroup = request.BloodGroup,
                    NoticePeriod = request.NoticePeriod,
                    RegionId =request.RegionId,
                    BankPassBook = Bankpassbook,
                    UAN_No = request.UAN_No,
                };

             
                CommonDBContext.EmployeeInfos.Add(newEmployee);
                await CommonDBContext.SaveChangesAsync();

              
                return new VivifyResponse<dynamic>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee successfully added.",
                    Result = new { EmpNo = newEmployee.EmpNo }
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<dynamic>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("UpdateEmployee")]
        public async Task<VivifyResponse<bool>> UpdateEmployee([FromForm] EmployeeInfos request)
        {
            try
            {
              
                long EmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

            
                var existingEmployee = CommonDBContext.EmployeeInfos
                    .FirstOrDefault(x => x.CompanyId == CompanyID && x.EmpNo == request.EmpNo);

                if (existingEmployee == null)
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = false
                    };
                }

             
                if (request.PhotoPath != null)
                {
                    string photoPath = await SaveAttachmentFileAsync(request.PhotoPath); 
                    existingEmployee.PhotoPath = photoPath; 
                }
              

                if (request.BankPassBook != null)
                {
                    string Bankpassbook = await SaveAttachmentFileAsync(request.BankPassBook);
                    existingEmployee.BankPassBook = Bankpassbook;
                }
                existingEmployee.FirstName = request.EmpName;
                existingEmployee.LastName = request.LastName;
                existingEmployee.EmailId = request.Email;
                existingEmployee.Pwd = request.Pwd;
                existingEmployee.Designation = request.Designation;
                existingEmployee.BranchCode = request.Branch;
                existingEmployee.ContactNumber = request.ContactNumber;
                existingEmployee.EmergencyNumber = string.IsNullOrWhiteSpace(request.EmergencyNumber) ? null : request.EmergencyNumber;
                existingEmployee.Dob = request.DateOfBirth.HasValue ? DateOnly.FromDateTime(request.DateOfBirth.Value) : null;
                existingEmployee.Gender = request.Gender;
                existingEmployee.UptDate = DateTime.UtcNow;
                existingEmployee.UptBy = EmpNo;
                existingEmployee.ReportingEmpNo = string.IsNullOrWhiteSpace(request.ReportingEmpNo?.ToString()) ? null : request.ReportingEmpNo;

                existingEmployee.BuildingNo = request.BuildingNo;
                existingEmployee.Street = request.Street;
                existingEmployee.City = request.City;
                existingEmployee.Pincode = request.Pincode;
                existingEmployee.Country = string.IsNullOrWhiteSpace(request.Country) ? "India" : request.Country;
                existingEmployee.PersonalMail = string.IsNullOrWhiteSpace(request.PersonalMail) ? existingEmployee.PersonalMail : request.PersonalMail;
                existingEmployee.BankNo = string.IsNullOrWhiteSpace(request.BankNo) ? existingEmployee.BankNo : request.BankNo;
                existingEmployee.BankName = request.BankName;
                existingEmployee.IFSCCode = request.IFSCCode;
                existingEmployee.ContBuildingNo = request.ContBuildingNo;
                existingEmployee.ContStreet = request.ContStreet;
                existingEmployee.ContCity = request.ContCity;
                existingEmployee.ContPinCode = request.ContPinCode;
                existingEmployee.ContCountry = string.IsNullOrWhiteSpace(request.ContCountry) ? "India" : request.ContCountry;
                existingEmployee.BloodGroup = request.BloodGroup;
                existingEmployee.UAN_No = request.UAN_No;
                existingEmployee.RoleId = request.RoleId;
                existingEmployee.DateOfJoin = request.DateOfJoin.HasValue
                    ? request.DateOfJoin.Value
                    : (DateOnly?)null;
                existingEmployee.NoticePeriod = request.NoticePeriod;
                existingEmployee.RegionId = request.RegionId;
                CommonDBContext.EmployeeInfos.Update(existingEmployee);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee successfully updated.",
                    Result = true
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = false
                };
            }
        }
        [HttpPost]
        [ActionName("DeleteEmployee")]
        public async Task<VivifyResponse<bool>> DeleteEmployee([FromQuery] long EmpNo)
        {
            try
            {
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);
                long EmpNoFromClaims = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);

                var existingEmployee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(x => x.CompanyId == CompanyID && x.EmpNo == EmpNo);

                if (existingEmployee == null)
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = false
                    };
                }

             
                existingEmployee.IsActive = false; 

                existingEmployee.UptDate = DateTime.UtcNow; 

             
                existingEmployee.UptBy = EmpNoFromClaims;

                CommonDBContext.EmployeeInfos.Update(existingEmployee);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee successfully deleted.",
                    Result = true
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = false
                };
            }
        }

        [HttpPost]
        [ActionName("InsertBiometricException")]
        public VivifyResponse<string> InsertBiometricException(
     [FromQuery] long empNo,
     [FromQuery] int IsEligOutSidePunch,
     [FromBody] BiometricExceptionDates dates)
        {
            try
            {
                long LoggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                DateOnly fromDate = dates.FromDate ?? DateOnly.MinValue;
                DateOnly toDate = dates.ToDate ?? DateOnly.MinValue;

                var newException = new EmpBioException
                {
                    EmpNo = empNo,
                    IsEligOutSidePunch = IsEligOutSidePunch == 1,
                    CrtBy = LoggedInEmpNo,
                    CrtDate = DateTime.Now,
                    CompanyId = CompanyID,

                   
                    StartDate = fromDate,
                    EndDate = toDate
                };

                HRMSDBContext.EmpBioExceptions.Add(newException);
                HRMSDBContext.SaveChanges();

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Saved Successfully",
                    Result = "Biometric exception added."
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"{ex.Message} : {ex.StackTrace}",
                    Result = "Failed"
                };
            }
        }
       

        [HttpPost]
        [ActionName("SaveEmpShiftType")]
        public VivifyResponse<string> SaveEmpShiftType(
     [FromQuery] long empNo,
     [FromQuery] int shiftId)
        {
            try
            {
                long loggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

               
                var shiftHoursRecord = HRMSDBContext.MShiftHours
                    .FirstOrDefault(x => x.ShiftId == shiftId);

                if (shiftHoursRecord == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "Shift hours not found",
                        Result = "No matching shift hours in MShiftHour table."
                    };
                }

              
                double shiftHours = shiftHoursRecord.WorkingHours;

              
                var newShiftRecord = new SafetyToolBoxDL.HRMSDB.EmpShiftHour
                {
                    EmpNo = empNo,
                    ShiftId = shiftId,
                    ShiftHours = shiftHours,
                    CompanyId = companyId,
                    CrtBy = loggedInEmpNo,
                    CrtDate = DateTime.Now
                };

                
                HRMSDBContext.EmpShiftHours.Add(newShiftRecord);
                HRMSDBContext.SaveChanges();

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Inserted Successfully",
                    Result = "Shift hours added successfully."
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"{ex.Message} : {ex.StackTrace}",
                    Result = "Failed to insert shift hours."
                };
            }
        }




        [HttpPost]
        [ActionName("AddOrModifyDesignation")]
        public VivifyResponse<bool> AddOrModifyDesignation([FromBody] DesignationRequest request, int OperationType)
        {
            try
            {
                long EmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var existingDesignation = CommonDBContext.MDesignations
                    .FirstOrDefault(x => x.DesignationId == request.DesignationId && x.CompanyId == CompanyID);

               
                if (OperationType == 1)
                {
                    if (existingDesignation != null)
                    {
                        return new VivifyResponse<bool> { StatusCode = 409, StatusDesc = "Designation already exists.", Result = false };
                    }

                    var newDesignation = new MDesignation
                    {
                        CompanyId = CompanyID,
                        DesignationId = request.DesignationId,
                        DesignationDesc = request.DesignationName,
                        CrtBy = EmpNo,
                        CrtDate = DateTime.UtcNow,
                        UptBy = EmpNo,
                        UptDate = DateTime.UtcNow,
                        IsActive = true
                    };

                    CommonDBContext.MDesignations.Add(newDesignation);
                    CommonDBContext.SaveChanges();
                    return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Designation successfully added.", Result = true };
                }

               
                else if (OperationType == 2)
                {
                    if (existingDesignation == null)
                    {
                        return new VivifyResponse<bool> { StatusCode = 404, StatusDesc = "Designation not found.", Result = false };
                    }

                    existingDesignation.DesignationDesc = request.DesignationName;
                    existingDesignation.UptBy = EmpNo;
                    existingDesignation.UptDate = DateTime.UtcNow;

                    CommonDBContext.MDesignations.Update(existingDesignation);
                    CommonDBContext.SaveChanges();
                    return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Designation successfully modified.", Result = true };
                }

              
                else if (OperationType == 3)
                {
                    if (existingDesignation == null)
                    {
                        return new VivifyResponse<bool> { StatusCode = 404, StatusDesc = "Designation not found.", Result = false };
                    }

                    CommonDBContext.MDesignations.Remove(existingDesignation);
                    CommonDBContext.SaveChanges();
                    return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Designation successfully deleted.", Result = true };
                }

                return new VivifyResponse<bool> { StatusCode = 400, StatusDesc = "Invalid operation type.", Result = false };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = "Error: " + ex.Message, Result = false };
            }
        }
        [HttpPost]
        [ActionName("AddOrModifyShift")]
        public VivifyResponse<bool> AddOrModifyShift([FromBody] ShiftDetailsRequest request, int OpsType)
        {
            try
            {
                long EmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                MShiftHour shift = HRMSDBContext.MShiftHours.FirstOrDefault(x => x.CompanyId == CompanyID && x.ShiftId == request.ShiftId);

                if (shift != null && OpsType == 1)
                {
                    if (shift.IsActive == false)
                    {
                        return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = "Shift already exists with inactive status.", Result = false };
                    }
                    else
                    {
                        return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = "Shift already exists.", Result = false };
                    }
                }
                else
                {
                    MShiftHour newShift = null;

                    if (OpsType == 1)
                    {
                        newShift = new MShiftHour
                        {
                            CompanyId = CompanyID,
                            ShiftId = request.ShiftId,
                            ShiftName = request.ShiftName,
                            WorkingHours = request.ShiftHours,
                            IsActive = true,
                            CrtDate = DateTime.UtcNow,
                            CrtBy = EmpNo,
                            UptDate = DateTime.UtcNow,
                            UptBy = EmpNo
                        };

                        HRMSDBContext.MShiftHours.Add(newShift);
                        HRMSDBContext.SaveChanges();
                        return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Shift successfully added.", Result = true };
                    }

                    if (OpsType == 2)
                    {
                        MShiftHour existingShift = HRMSDBContext.MShiftHours .FirstOrDefault(x => x.ShiftId != request.ShiftId && x.ShiftName == request.ShiftName && x.CompanyId == CompanyID);

                        if (existingShift != null)
                        {
                            if (existingShift.IsActive == false)
                            {
                                return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = "Shift already exists with inactive status.", Result = false };
                            }
                            else
                            {
                                return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = "Shift already exists.", Result = false };
                            }
                        }

                        newShift = HRMSDBContext.MShiftHours .FirstOrDefault(x => x.ShiftId == request.ShiftId && x.CompanyId == CompanyID);

                        if (newShift == null)
                        {
                            return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = "No shift ID found to modify.", Result = false };
                        }

                        newShift.ShiftName = request.ShiftName;
                        newShift.WorkingHours = request.ShiftHours;
                        newShift.UptDate = DateTime.UtcNow;
                        newShift.UptBy = EmpNo;

                        HRMSDBContext.Entry(newShift).State = EntityState.Modified;
                        HRMSDBContext.SaveChanges();
                        return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Shift successfully modified.", Result = true };
                    }

                   
                    else
                    {
                        newShift = HRMSDBContext.MShiftHours.FirstOrDefault(x => x.CompanyId == CompanyID && x.ShiftId == request.ShiftId);

                        if (newShift == null)
                        {
                            return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = "Shift not found.", Result = false };
                        }

                        newShift.IsActive = false;                  
                        newShift.UptDate = DateTime.UtcNow;
                        newShift.UptBy = EmpNo;

                        HRMSDBContext.Entry(newShift).State = EntityState.Modified;
                        HRMSDBContext.SaveChanges();
                        return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Shift successfully deleted.", Result = true };
                    }
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = $"{ex.Message}: {ex.StackTrace}", Result = false };
            }
        }
        [HttpGet]
        [ActionName("GetLeaveBalanceSummary")]
        public VivifyResponse<List<LeaveBalanceSummary>> GetLeaveBalanceSummary()
        {
            try
            {
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);

                var summary = (from lt in HRMSDBContext.LeaveTypes
                               join lb in HRMSDBContext.LeaveBalances
                                   .Where(x => x.EmpNo == empNo && !x.Expired)
                                   on lt.Id.ToString() equals lb.LeaveTypeId into lbGroup
                               select new LeaveBalanceSummary
                               {
                                   LeaveTypeName = lt.LeaveType,
                                   RemainingDays = Math.Max(0, lt.NoOfDays - lbGroup.Sum(x => Math.Abs(x.NoOfLeaveDays))) // Ensure no negative remaining days
                               }).ToList();

                return new VivifyResponse<List<LeaveBalanceSummary>>
                {
                    StatusCode = 200,
                    StatusDesc = "Leave balance summary loaded successfully.",
                    Result = summary
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<LeaveBalanceSummary>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new List<LeaveBalanceSummary>()
                };
            }
        }

        [HttpGet]
        [ActionName("GetAllRequestDetails")]
        public VivifyResponse<object> GetAllRequestDetails()
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                int totalEmployees = CommonDBContext.EmployeeInfos
                    .Where(e => e.CompanyId == companyId)
                    .Count();

                int totalLeaveRequests = HRMSDBContext.LeaveRequests
                    .Count(lr => lr.CompanyId == companyId && lr.Status == "0");

                int totalAdvanceRequests = HRMSDBContext.Advances
                    .Count(ar => ar.CompanyID == companyId && ar.Status == "0");

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Counts retrieved successfully.",
                    Result = new
                    {
                        TotalEmployees = totalEmployees,
                        TotalLeaveRequests = totalLeaveRequests,
                        TotalAdvanceRequests = totalAdvanceRequests
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new { Message = "Unable to retrieve counts due to an error." }
                };
            }
        }
      


        [HttpPost]
        [ActionName("AddOrModifyBranches")]
        public VivifyResponse<bool> AddOrModifyBranches([FromBody] BranchDetailsRequest request, int OpsType)
        {
            try
            {
                long EmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                MBranch existingBranch = CommonDBContext.MBranches
                    .FirstOrDefault(x => x.CompanyId == CompanyID && x.BranchCode == request.BranchCode);

                if (OpsType == 1)
                {
                    if (existingBranch != null)
                    {
                        if (!existingBranch.IsActive)
                        {
                            return new VivifyResponse<bool>
                            {
                                StatusCode = 500,
                                StatusDesc = "Branch already exists with inactive status.",
                                Result = false
                            };
                        }
                        else
                        {
                            return new VivifyResponse<bool>
                            {
                                StatusCode = 500,
                                StatusDesc = "Branch already exists.",
                                Result = false
                            };
                        }
                    }

                    MBranch newBranch = new MBranch
                    {
                        CompanyId = CompanyID,
                        BranchCode = request.BranchCode,
                        BranchDesc = request.BranchDesc,
                        Geofencing = request.Geofencing,
                        SiteType = request.SiteType,
                        IsActive = true,
                        CrtDate = DateTime.Now,
                        CrtBy = EmpNo,
                        UptDate = DateTime.Now,
                        UptBy = EmpNo
                    };

                    CommonDBContext.MBranches.Add(newBranch);
                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<bool>
                    {
                        StatusCode = 200,
                        StatusDesc = "Branch successfully added.",
                        Result = true
                    };
                }

                if (OpsType == 2)
                {
                    MBranch branchToUpdate = CommonDBContext.MBranches
                        .FirstOrDefault(x => x.BranchCode == request.BranchCode && x.CompanyId == CompanyID);

                    if (branchToUpdate == null)
                    {
                        return new VivifyResponse<bool>
                        {
                            StatusCode = 500,
                            StatusDesc = "No branch found to modify.",
                            Result = false
                        };
                    }

                    branchToUpdate.BranchDesc = request.BranchDesc;
                    branchToUpdate.Geofencing = request.Geofencing;
                    branchToUpdate.SiteType = request.SiteType;
                    branchToUpdate.UptDate = DateTime.Now;
                    branchToUpdate.UptBy = EmpNo;

                    CommonDBContext.Entry(branchToUpdate).State = EntityState.Modified;
                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<bool>
                    {
                        StatusCode = 200,
                        StatusDesc = "Branch successfully modified.",
                        Result = true
                    };
                }

               
                if (OpsType == 3)
                {
                   
                    MBranch branchToDelete = CommonDBContext.MBranches
                        .FirstOrDefault(x => x.BranchCode == request.BranchCode && x.CompanyId == CompanyID);

                    if (branchToDelete == null)
                    {
                        return new VivifyResponse<bool>
                        {
                            StatusCode = 500,
                            StatusDesc = "Branch not found.",
                            Result = false
                        };
                    }

                  
                    branchToDelete.IsActive = false;
                    branchToDelete.UptDate = DateTime.Now;
                    branchToDelete.UptBy = EmpNo;

                  
                    CommonDBContext.Entry(branchToDelete).State = EntityState.Modified;
                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<bool>
                    {
                        StatusCode = 200,
                        StatusDesc = "Branch successfully deleted.",
                        Result = true
                    };
                }
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = "Invalid operation type.",
                    Result = false
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"{ex.Message}: {ex.StackTrace}",
                    Result = false
                };
            }
        }
       

       
    }
}
