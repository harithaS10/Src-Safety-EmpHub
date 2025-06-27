using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client.Extensions.Msal;
using SafetyToolBoxAPI.Common;
using SafetyToolBoxDL.CommonDB;
using SafetyToolBoxDL.CustomDB;
using SafetyToolBoxDL.HRMSDB;
using SafetyToolBoxDL.SafetyDB;

namespace SafetyToolBoxAPI.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly VivifyCommonContext CommonDBContext;
        private readonly VivifySafetyToolboxContext SafetyDBContext;
        private readonly CustomDBContext CustDBContext;
        private readonly VivifyHrmsContext HRMSDBContext;

        private IConfiguration Config;
        public DashboardController(VivifyCommonContext context, VivifySafetyToolboxContext SafteyContxt, IConfiguration _config, CustomDBContext _custObject, VivifyHrmsContext HrmsContext)
        {
            CommonDBContext = context;
            SafetyDBContext = SafteyContxt;
            CustDBContext = _custObject;
            Config = _config;
            HRMSDBContext = HrmsContext;
        }
        [HttpGet]
        [ActionName("GetDashboardInfo")]
        public VivifyResponse<DashboardDisplay> GetDashboardInfo()
        {
            try
            {
                long Empno =Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                DashboardDisplay ObjDashboardInfo = new DashboardDisplay();
                var EmpInfoList = CustDBContext.DashBoardInfo.FromSqlRaw("exec SPR_GetDashboardInfo " + CompanyID+","+Empno).ToList();
                
                if (EmpInfoList == null)
                {
                    return new VivifyResponse<DashboardDisplay> { StatusCode = 400, StatusDesc = "Employee is not found" };
                }
                else
                {
                    var EmpInfo = EmpInfoList[0];
                    ObjDashboardInfo.Name = EmpInfo.Name;
                    ObjDashboardInfo.Designation = EmpInfo.Designation;
                    ObjDashboardInfo.TrainingStatus = EmpInfo.TrainingStatus;
                    ObjDashboardInfo.AttendanceStatus = EmpInfo.AttendanceStatus;
                    ObjDashboardInfo.DashoardConfirmation = EmpInfo.DashoardConfirmation;
                    ObjDashboardInfo.PhotoPath = EmpInfo.PhotoPath;
                    ObjDashboardInfo.DisplayInfo = SafetyDBContext.DashboardInfos.Select(t => t.DashboardQuest).ToList();

                    return new VivifyResponse<DashboardDisplay> { StatusCode = 200, StatusDesc = "Success", Result = ObjDashboardInfo };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<DashboardDisplay> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }
        [HttpGet]
        [ActionName("GetDashboardQuestions")]
        public VivifyResponse<List<string>> GetDashboardQuestions(long? EmpNo = null)
        {
            try
            {
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);


                //var dashboardQuestions = CommonDBContext.DashboardSafetyFQuest

                //    .Where(q => q.CompanyID == CompanyID && q.EmpNo == Empno)
                //    .Select(q => q.DashboardQuest)
                //    .ToList();

                //if (dashboardQuestions == null || !dashboardQuestions.Any())
                //{
                //    return new VivifyResponse<List<string>>
                //    {
                //        StatusCode = 404,
                //        StatusDesc = "No dashboard questions found for this company and employee."
                //    };
                //}

                return new VivifyResponse<List<string>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = null
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<string>>
                {
                    StatusCode = 500,
                    StatusDesc = $"{ex.Message}: {ex.StackTrace}"
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmpHRMSDashBoard")]
        public VivifyResponse<object> GetEmpHRMSDashBoard()
        {
            try
            {
                // Get the EmpNo and CompanyID from the claims in the token
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int companyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                // Query for latest advance
                var advancesQuery = from adv in HRMSDBContext.Advances
                                    join advType in HRMSDBContext.AdvanceTypes
                                    on adv.AdvanceTypeID equals advType.AdvanceTypeID into advGroup
                                    from advType in advGroup.DefaultIfEmpty()
                                    where adv.CompanyID == companyID && adv.EmpNo == empNo
                                    select new
                                    {
                                        adv.EmpNo,
                                        adv.BranchCode,
                                        adv.Status,
                                        adv.ReqDate,
                                        adv.Remarks,
                                        adv.AdvanceTypeID,
                                        AdvanceTypeDesc = advType != null ? advType.AdvanceDesc : "N/A",
                                        adv.ApprAmnt,
                                        adv.ApproveCmnt
                                    };

                // Include all statuses including "0"
                var latestAdvance = advancesQuery
                    .OrderByDescending(a => a.ReqDate)
                    .FirstOrDefault();

                // Query for latest leave request
                var leaveRequestQuery = from leave in HRMSDBContext.LeaveRequests
                                        join leaveType in HRMSDBContext.LeaveTypes
                                        on leave.LeaveTypeId equals leaveType.LeaveTypeId into leaveGroup
                                        from leaveType in leaveGroup.DefaultIfEmpty()
                                        where leave.CompanyId == companyID && leave.EmpNo == empNo
                                        select new
                                        {
                                            leave.EmpNo,
                                            leave.BranchId,
                                            leave.Status,
                                            leave.Crt_Date,
                                            leave.Remarks,
                                            leave.LeaveTypeId,
                                            LeaveTypeDesc = leaveType != null ? leaveType.LeaveType : "N/A",
                                            leave.FromDate,
                                            leave.ToDate
                                        };

                var latestLeaveRequest = leaveRequestQuery
                    .OrderByDescending(l => l.Crt_Date)
                    .FirstOrDefault();

                if (latestAdvance == null && latestLeaveRequest == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No records found.",
                        Result = new { message = "No records found." }
                    };
                }

                var employeeInfo = CommonDBContext.EmployeeInfos.FirstOrDefault(e => e.EmpNo == empNo);

                var response = new
                {
                    Advance = latestAdvance != null ? new
                    {
                        EmpName = $"{latestAdvance.EmpNo} - {employeeInfo?.FirstName ?? "Unknown"}",
                        BranchCode = latestAdvance.BranchCode,
                        Status = latestAdvance.Status,
                        ReqDate = latestAdvance.ReqDate,
                        Remarks = latestAdvance.Remarks ?? string.Empty,
                        AdvanceTypeID = latestAdvance.AdvanceTypeID,
                        AdvanceTypeDesc = latestAdvance.AdvanceTypeDesc,
                        ApproveAmnt = latestAdvance.Status == "1" ? latestAdvance.ApprAmnt : null,
                        ApproveCmnt = latestAdvance.Status == "1" ? latestAdvance.ApproveCmnt : null
                    } : null,

                    LeaveRequest = latestLeaveRequest != null ? new
                    {
                        EmpName = $"{latestLeaveRequest.EmpNo} - {employeeInfo?.FirstName ?? "Unknown"}",
                        BranchCode = latestLeaveRequest.BranchId,
                        Status = latestLeaveRequest.Status,
                        RequestDate = latestLeaveRequest.Crt_Date,
                        Remarks = latestLeaveRequest.Remarks ?? string.Empty,
                        LeaveTypeID = latestLeaveRequest.LeaveTypeId,
                        LeaveTypeDesc = latestLeaveRequest.LeaveTypeDesc,
                        FromDate = latestLeaveRequest.FromDate,
                        ToDate = latestLeaveRequest.ToDate
                    } : null
                };

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = response
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
        [ActionName("GetDDEmployeeInfo")]
        public VivifyResponse<List<DDEmployeeInfo>> GetDDEmployeeInfo(string SearchWord)
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);
                List<DDEmployeeInfo> ObjDashboardInfo = new List<DDEmployeeInfo>();
                ObjDashboardInfo = CommonDBContext.EmployeeInfos.Where(x => x.FirstName.Contains(SearchWord) && x.CompanyId==CompanyID).Select(t => new DDEmployeeInfo { EmpNo = t.EmpNo, Name = t.FirstName + ' ' + t.LastName }).ToList();

                if (ObjDashboardInfo == null)
                {
                    return new VivifyResponse<List<DDEmployeeInfo>> { StatusCode = 400, StatusDesc = "Employee is not found" };
                }
                else
                {
                    return new VivifyResponse<List<DDEmployeeInfo>> { StatusCode = 200, StatusDesc = "Success", Result = ObjDashboardInfo };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<DDEmployeeInfo>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }
        [HttpGet]
        [ActionName("UpdateDashboardConf")]
        public VivifyResponse<bool> UpdateDashboardConf()
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                DashboardConfirm dbConf=new DashboardConfirm();
                dbConf.EmpNo=Empno;
                dbConf.BusinessDate= DateOnly.FromDateTime(DateTime.Now.AddDays(1));
                dbConf.ConfirmedTime = DateTime.Now;
                dbConf.CompanyId= CompanyID;
                dbConf.CrtDate = DateTime.Now;
                SafetyDBContext.DashboardConfirms.Add(dbConf);
                SafetyDBContext.SaveChanges();
                return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Successfully Updated", Result = true };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }

        [HttpGet]
        [ActionName("GetBranches")]
        public VivifyResponse<List<BranchDisplay>> GetBranches()
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                List<BranchDisplay> lstMBranches = CommonDBContext.MBranches.Where(x=>x.CompanyId== CompanyID).Select(x => new BranchDisplay { BranchCode = x.BranchCode, BranchDesc = x.BranchDesc }).ToList();

                return new VivifyResponse<List<BranchDisplay>> { StatusCode = 200, StatusDesc = "Loded Successfully", Result = lstMBranches };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<BranchDisplay>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }

        [HttpGet]
        [ActionName("GetRoles")]
        public VivifyResponse<List<RoleDisplay>> GetRoles()
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                List<RoleDisplay> lstMBranches = CommonDBContext.MUserRoles.Where(x => x.CompanyId == CompanyID).Select(x => new RoleDisplay { RoleID = x.RoleId, RoleDesc = x.RoleName }).ToList();

                return new VivifyResponse<List<RoleDisplay>> { StatusCode = 200, StatusDesc = "Loded Successfully", Result = lstMBranches };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<RoleDisplay>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }
    }
}
