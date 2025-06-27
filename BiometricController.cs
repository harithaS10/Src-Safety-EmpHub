using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SafetyToolBoxAPI.Common;
using SafetyToolBoxDL.CommonDB;
using SafetyToolBoxDL.CustomDB;
using SafetyToolBoxDL.HRMSDB;
using SafetyToolBoxDL.SafetyDB;
using static SafetyToolBoxAPI.Common.RejectMissPunchRequestDto;


namespace SafetyToolBoxAPI.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    [Authorize]
    public class BiometricController : ControllerBase
    {
        private readonly VivifyHrmsContext HRMSDBContext;
        private readonly CustomDBHRMSContext HRMSCustomDBContext;
        private readonly VivifyCommonContext CommonDBContext;
        private IConfiguration Config;

        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public BiometricController(VivifyHrmsContext _HRMSDBContext, IConfiguration _config, CustomDBHRMSContext _CustomDBHRMSContext, VivifyCommonContext _commonDBContext)
        {
            HRMSDBContext = _HRMSDBContext;
            HRMSCustomDBContext = _CustomDBHRMSContext;
            CommonDBContext = _commonDBContext;
            Config = _config;
        }
        
        [HttpGet]
        [ActionName("GetCurrentCheckIn")]
        public VivifyResponse<Tuple<DateTime?, DateTime?>> GetCurrentCheckIn()
                    {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                long CompanyID = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                DateOnly dt = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE));                
                var bioInfo = HRMSDBContext.EmpBiometrics.Where(x => x.EmpNo == Empno && x.BusinessDate== dt && x.CheckInType==1 && x.CompanyId== CompanyID).OrderByDescending(x=>x.Id).FirstOrDefault();
                var bioInfoOut = HRMSDBContext.EmpBiometrics.Where(x => x.EmpNo == Empno && x.BusinessDate == dt && x.CheckInType == 2 && x.CompanyId == CompanyID).OrderByDescending(x => x.Id).FirstOrDefault();
                if (bioInfo == null)
                {
                    return new VivifyResponse<Tuple<DateTime?, DateTime?>> { StatusCode = 400, StatusDesc = "No CheckIn Found", Result = new Tuple<DateTime?, DateTime?>(null,null) };
                }
                else
                {
                    var retObj = new Tuple<DateTime?, DateTime?>(bioInfo.CheckInTime,bioInfoOut?.CheckInTime);
                    return new VivifyResponse<Tuple<DateTime?, DateTime?>> { StatusCode = 200, StatusDesc = "Success", Result = retObj };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<Tuple<DateTime?, DateTime?>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }
        [HttpPost]
        [ActionName("CheckInAndOut")]
        public VivifyResponse<bool> CheckInAndOut(Biometric ObjBiometric)
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);
                string Branch = Convert.ToString(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Branch")?.Value);

                // Get biometric exception entry for employee
                EmpBioException empBioException = HRMSDBContext.EmpBioExceptions
                    .Where(x => x.EmpNo == Empno && x.CompanyId == CompanyID)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefault();

                // Determine if geofence check is needed
                bool shouldCheckGeofence = ObjBiometric.IsEligOutSidePunch.HasValue
                    ? !ObjBiometric.IsEligOutSidePunch.Value
                    : (empBioException != null && !empBioException.IsEligOutSidePunch);

                // Perform geofencing validation only if required
                if (shouldCheckGeofence)
                {
                    MBranch ObjGeoFence = CommonDBContext.MBranches
                        .Where(x => x.BranchCode == Branch)
                        .OrderByDescending(x => x.Id)
                        .FirstOrDefault();

                    if (ObjGeoFence != null && !string.IsNullOrEmpty(ObjGeoFence.Geofencing))
                    {
                        bool ret = Geo.checkPointExistsInGeofencePolygon(ObjGeoFence.Geofencing, ObjBiometric.Lat, ObjBiometric.Long);
                        if (!ret)
                        {
                            return new VivifyResponse<bool>
                            {
                                StatusCode = 200,
                                StatusDesc = "PunchIn and PunchOut should be within office premises",
                                Result = false
                            };
                        }
                    }
                }

                DateOnly dt = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE));
                DateTime dtime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE);

                // Get shift hours
                EmpShiftHour ObjEmpShiftHour = HRMSDBContext.EmpShiftHours
                    .Where(x => x.EmpNo == Empno && x.CompanyId == CompanyID)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefault();

                if (ObjEmpShiftHour == null)
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 200,
                        StatusDesc = "Shift hours are not maintained.",
                        Result = false
                    };
                }

                string? RefCol = Guid.NewGuid().ToString();

                // Handle Punch-Out logic
                if (ObjBiometric.TypeOfCheckIn == 2)
                {
                    var Verification = HRMSDBContext.EmpBiometrics
                        .Where(x => x.EmpNo == Empno && x.CompanyId == CompanyID && x.CheckInType == 1)
                        .OrderByDescending(x => x.Id)
                        .FirstOrDefault();

                    if (Verification == null)
                    {
                        return new VivifyResponse<bool>
                        {
                            StatusCode = 200,
                            StatusDesc = "There is no punchin available. Please do punchin then punchout",
                            Result = false
                        };
                    }

                    var existingPunchOut = HRMSDBContext.EmpBiometrics
                        .Any(x => x.EmpNo == Empno && x.CompanyId == CompanyID &&
                                  x.CheckInRef == Verification.CheckInRef && x.CheckInType == 2);

                    if (existingPunchOut)
                    {
                        return new VivifyResponse<bool>
                        {
                            StatusCode = 200,
                            StatusDesc = "Punch-out already recorded for this punch-in.",
                            Result = false
                        };
                    }

                    RefCol = Verification.CheckInRef;
                    dt = Verification.BusinessDate;
                }

                // Handle Punch-In logic
                if (ObjBiometric.TypeOfCheckIn == 1)
                {
                    var latestPunch = HRMSDBContext.EmpBiometrics
                        .Where(x => x.EmpNo == Empno && x.CompanyId == CompanyID)
                        .OrderByDescending(x => x.Id)
                        .FirstOrDefault();

                    if (latestPunch != null && latestPunch.CheckInType == 1)
                    {
                        TimeSpan timeDifference = dtime - latestPunch.CheckInTime.Value;

                        if (timeDifference.TotalMinutes < 5)
                        {
                            return new VivifyResponse<bool>
                            {
                                StatusCode = 200,
                                StatusDesc = "Already punch-in done. Please do punch-out first.",
                                Result = false
                            };
                        }
                    }
                }

                // Save biometric record
                EmpBiometric objEmpBiometric = new EmpBiometric
                {
                    EmpNo = Empno,
                    CompanyId = CompanyID,
                    BusinessDate = dt,
                    CheckInTime = dtime,
                    CheckInType = ObjBiometric.TypeOfCheckIn,
                    CheckInRef = RefCol,
                    ShiftHours = ObjEmpShiftHour.ShiftHours,
                    GeoLatAndLong = $"{ObjBiometric.Lat}|{ObjBiometric.Long}",
                    PunchInLocation = ObjBiometric.Location
                };

                HRMSDBContext.EmpBiometrics.Add(objEmpBiometric);

                // Only insert or update EmpBioException if IsEligOutSidePunch was passed
                if (ObjBiometric.IsEligOutSidePunch.HasValue)
                {
                    if (empBioException != null)
                    {
                        empBioException.IsEligOutSidePunch = ObjBiometric.IsEligOutSidePunch.Value;
                        empBioException.CrtDate = dtime;
                        empBioException.CrtBy = Empno;
                    }
                    else
                    {
                        EmpBioException newException = new EmpBioException
                        {
                            EmpNo = Empno,
                            CompanyId = CompanyID,
                            IsEligOutSidePunch = ObjBiometric.IsEligOutSidePunch.Value,
                            StartDate = dt,
                            EndDate = dt,
                            CrtDate = dtime,
                            CrtBy = Empno
                        };
                        HRMSDBContext.EmpBioExceptions.Add(newException);
                    }
                }

                HRMSDBContext.SaveChanges();

                string responseMessage = ObjBiometric.TypeOfCheckIn == 1
                    ? "Successfully marked PunchIn"
                    : "Successfully marked PunchOut";

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = responseMessage,
                    Result = true
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = false
                };
            }
        }



        [HttpGet]
        [ActionName("GetRemarksDetails")]
        public VivifyResponse<object> GetRemarksDetails([FromQuery] long empNo, [FromQuery] DateTime reqDate)
        {
            try
            {
               
                long companyId = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var record = HRMSDBContext.MissPunches
                    .FirstOrDefault(x => x.EmpNo == empNo
                                      && x.CompanyId == companyId
                                      && x.ReqDate.Date == reqDate.Date
                                      && x.Status == "1");

                if (record == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No record found for the specified employee, date, and status.",
                        Result = null
                    };
                }

               
                var result = new
                {
                    EmpNo = record.EmpNo,
                    CompanyId = record.CompanyId,
                    PunchInTime = record.PunchInTime,
                    PunchOutTime = record.PunchOutTime,
                    ReqDate = record.ReqDate,
                    Remarks = record.Remarks ?? "No remarks available",
                    //IsActive = record.IsActive,
                    Status = record.Status
                };

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetTotalShiftDetails")]
        public VivifyResponse<object> GetTotalShiftDetails(
      [FromQuery] string fromDate = "",
      [FromQuery] string toDate = "",
      [FromQuery] string empNo = "",
      [FromQuery] string branchCode = "",
      [FromQuery] string status = "")
        {
            try
            {
                long LoggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                DateOnly? fromDateFilter = string.IsNullOrEmpty(fromDate) ? null : DateOnly.Parse(fromDate);
                DateOnly? toDateFilter = string.IsNullOrEmpty(toDate) ? null : DateOnly.Parse(toDate);
                long? empNoFilter = string.IsNullOrEmpty(empNo) ? null : long.Parse(empNo);

                var empBiometrics = HRMSDBContext.EmpBiometrics
                    .Where(eb =>
                        (empNoFilter == null || eb.EmpNo == empNoFilter) &&
                        (fromDateFilter == null || eb.BusinessDate >= fromDateFilter) &&
                        (toDateFilter == null || eb.BusinessDate <= toDateFilter))
                    .ToList();

                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Where(ei =>
                        (string.IsNullOrEmpty(branchCode) || ei.BranchCode == branchCode) &&
                        ei.CompanyId == CompanyID)
                    .ToList();

                // 🔁 Filter MissPunches based on status parameter
                var missPunchesQuery = HRMSDBContext.MissPunches
                    .Where(mp =>
                        (fromDateFilter == null || mp.ReqDate >= fromDateFilter.Value.ToDateTime(TimeOnly.MinValue)) &&
                        (toDateFilter == null || mp.ReqDate <= toDateFilter.Value.ToDateTime(TimeOnly.MaxValue)) &&
                        (empNoFilter == null || mp.EmpNo == empNoFilter));

                if (string.IsNullOrEmpty(status))
                {
                    missPunchesQuery = missPunchesQuery.Where(mp => mp.Status == "0" || mp.Status == "2");
                }
                else
                {
                    missPunchesQuery = missPunchesQuery.Where(mp => mp.Status == status);
                }

                var missPunches = missPunchesQuery
                    .Select(mp => new
                    {
                        mp.EmpNo,
                        mp.ReqDate,
                        mp.Remarks,
                        mp.Status,
                        mp.ApproveCmt
                    })
                    .ToList();

                var rawData = empBiometrics
                    .Join(employeeInfos, eb => eb.EmpNo, ei => ei.EmpNo, (eb, ei) => new
                    {
                        eb.Id,
                        eb.EmpNo,
                        eb.BusinessDate,
                        eb.CheckInType,
                        eb.CheckInTime,
                        eb.ShiftHours,
                        ei.BranchCode,
                        ei.CompanyId,
                        FirstName = ei.FirstName
                    })
                    .GroupJoin(missPunches,
                        eb => new { EmpNo = eb.EmpNo, BusinessDate = eb.BusinessDate },
                        mp => new { EmpNo = mp.EmpNo, BusinessDate = DateOnly.FromDateTime(mp.ReqDate) },
                        (eb, mpGroup) => new
                        {
                            eb.Id,
                            eb.EmpNo,
                            eb.BusinessDate,
                            eb.CheckInType,
                            eb.CheckInTime,
                            eb.ShiftHours,
                            eb.BranchCode,
                            eb.CompanyId,
                            eb.FirstName,
                            MissPunch = mpGroup.FirstOrDefault()
                        })
                    .ToList();

                var shiftDetails = rawData
                    .GroupBy(eb => new
                    {
                        eb.EmpNo,
                        CompanyID = eb.CompanyId,
                        BusinessDate = eb.BusinessDate,
                        BranchCode = eb.BranchCode
                    })
                    .Select(g => new
                    {
                        EmpNo = g.Key.EmpNo,
                        CompanyID = g.Key.CompanyID,
                        BusinessDate = g.Key.BusinessDate,
                        BranchCode = g.Key.BranchCode,
                        FirstCheckIn = g.Where(eb => eb.CheckInType == 1)
                                        .OrderBy(eb => eb.CheckInTime)
                                        .Select(eb => eb.CheckInTime)
                                        .FirstOrDefault(),
                        LastCheckOut = g.Where(eb => eb.CheckInType == 2)
                                        .OrderByDescending(eb => eb.CheckInTime)
                                        .Select(eb => eb.CheckInTime)
                                        .FirstOrDefault(),
                        TotalHours = CalculateTotalHours(
                            g.Where(eb => eb.CheckInType == 1).OrderBy(eb => eb.CheckInTime).Select(eb => eb.CheckInTime).FirstOrDefault(),
                            g.Where(eb => eb.CheckInType == 2).OrderByDescending(eb => eb.CheckInTime).Select(eb => eb.CheckInTime).FirstOrDefault()
                        ),
                        Remarks = g.Select(eb => eb.MissPunch?.Remarks).FirstOrDefault(),
                        ApproveCmnt = g.Select(eb => eb.MissPunch?.ApproveCmt).FirstOrDefault(),
                        Status = g.Select(eb => eb.MissPunch?.Status).FirstOrDefault(),
                        ReqDate = g.Select(eb => eb.MissPunch?.ReqDate).FirstOrDefault(),
                        FirstName = g.Select(eb => eb.FirstName).FirstOrDefault()
                    })
                    .Where(sd => sd.Status == "0" || sd.Status == "2")
                    .ToList();

                if (shiftDetails.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Data loaded successfully",
                        Result = shiftDetails.Select(sd => new
                        {
                            sd.EmpNo,
                            sd.CompanyID,
                            sd.BusinessDate,
                            sd.BranchCode,
                            sd.FirstCheckIn,
                            sd.LastCheckOut,
                            TotalHours = Math.Round(sd.TotalHours, 2),
                            sd.Remarks,
                            sd.ApproveCmnt,
                            sd.Status,
                            sd.ReqDate,
                            FirstName = $"{sd.EmpNo} - {sd.FirstName}"
                        })
                    };
                }
                else
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No shift details found.",
                        Result = new { Message = "No records found." }
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { StackTrace = ex.StackTrace }
                };
            }
        }



        private double CalculateTotalHours(DateTime? firstCheckIn, DateTime? lastCheckOut)
        {
            if (firstCheckIn == null || lastCheckOut == null)
            {
                return 0; 
            }

            DateTime startDateTime = ((DateTime)firstCheckIn).Date.Add(((DateTime)firstCheckIn).TimeOfDay);
            DateTime endDateTime = ((DateTime)lastCheckOut).Date.Add(((DateTime)lastCheckOut).TimeOfDay);

            if (endDateTime < startDateTime)
            {
                endDateTime = endDateTime.AddDays(1); 
            }

            TimeSpan totalTime = endDateTime - startDateTime;
            return totalTime.TotalHours;
        }

        [HttpGet]
        [ActionName("GetReqDetails")]
        public VivifyResponse<object> GetReqDetails(DateOnly? fromDate = null, DateOnly? toDate = null, string branchCode = "", long empNo = 0)
        {
            try
            {
                long LoggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Where(ei =>
                        (string.IsNullOrEmpty(branchCode) || ei.BranchCode == branchCode) &&
                        ei.CompanyId == CompanyID)
                    .ToList();

                var missPunches = HRMSDBContext.MissPunches
                    .Where(mp =>
                        (fromDate == null || mp.ReqDate >= fromDate.Value.ToDateTime(TimeOnly.MinValue)) &&
                        (toDate == null || mp.ReqDate <= toDate.Value.ToDateTime(TimeOnly.MaxValue)) &&
                        (empNo == 0 || mp.EmpNo == empNo) &&
                        mp.Status == "1")
                    .ToList();

                var shiftDetails = missPunches
                    .Join(employeeInfos, mp => mp.EmpNo, ei => ei.EmpNo, (mp, ei) => new
                    {
                        mp.EmpNo,
                        mp.CompanyId,
                        ei.BranchCode,
                        mp.ReqDate,
                        mp.PunchInTime,
                        mp.PunchOutTime,
                        mp.Remarks,
                        mp.Status,
                        mp.Crt_Date, // ✅ Include Crt_Date here
                        ei.FirstName
                    })
                    .ToList();

                var result = shiftDetails.Select(sd => new
                {
                    sd.EmpNo,
                    sd.CompanyId,
                    BusinessDate = DateOnly.FromDateTime(sd.ReqDate),
                    sd.BranchCode,
                    PunchInTime = sd.PunchInTime,
                    PunchOutTime = sd.PunchOutTime,
                    TotalHours = CalculateTotalHours(sd.PunchInTime, sd.PunchOutTime),
                    sd.Remarks,
                    sd.Status,
                    sd.ReqDate,
                    CrtDate = sd.Crt_Date,
                    FirstName = $"{sd.EmpNo} - {sd.FirstName}"
                });

                if (result.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Data loaded successfully",
                        Result = result
                    };
                }
                else
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No shift details found for the given filters.",
                        Result = new { }
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { StackTrace = ex.StackTrace }
                };
            }
        }



        [HttpGet]
        [ActionName("GetMissPunchReport")]
        public VivifyResponse<object> GetMissPunchReport(DateOnly? fromDate = null, DateOnly? toDate = null)
        {
            try
            {
                // Retrieve logged-in user details
                long LoggedInEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

              
                if (!fromDate.HasValue || !toDate.HasValue)
                {
                    fromDate = new DateOnly(1900, 1, 1); 
                    toDate = new DateOnly(9999, 12, 31); 
                }

               
                var missPunches = HRMSDBContext.MissPunches
                    .Where(mp =>
                        mp.EmpNo == LoggedInEmpNo &&  // Filter by logged-in employee
                        mp.ReqDate >= fromDate.Value.ToDateTime(TimeOnly.MinValue) &&
                        mp.ReqDate <= toDate.Value.ToDateTime(TimeOnly.MaxValue))
                    .ToList();

                // Fetch employee information for the logged-in user's company (optional, but keeps the same company filter)
                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Where(ei => ei.CompanyId == CompanyID && ei.EmpNo == LoggedInEmpNo)
                    .ToList();

                // Join miss punch records with employee information
                var reportData = missPunches
                    .Join(employeeInfos,
                          mp => mp.EmpNo,
                          ei => ei.EmpNo,
                          (mp, ei) => new
                          {
                              mp.EmpNo,
                              ei.FirstName,
                              ei.BranchCode,
                              ReqDate = DateOnly.FromDateTime(mp.ReqDate),
                              mp.Remarks,
                              mp.Status,
                              mp.ApproveCmt,
                              PunchInTime = mp.PunchInTime,
                              PunchOutTime = mp.PunchOutTime
                          })
                    .ToList();

                if (reportData.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Miss Punch Report loaded successfully",
                        Result = reportData.Select(rd => new
                        {
                            rd.EmpNo,
                            rd.FirstName,
                            rd.BranchCode,
                            rd.ReqDate,
                            rd.Remarks,
                            rd.Status,
                            rd.ApproveCmt,
                            PunchInTime = FormatTime(rd.PunchInTime),
                            PunchOutTime = FormatTime(rd.PunchOutTime)
                        }).ToList()
                    };
                }
                else
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No miss punch records found for the logged-in employee within the given filters.",
                        Result = new { Message = "No records found." }
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { StackTrace = ex.StackTrace }
                };
            }
        }

        private string FormatTime(DateTime? dateTime)
        {
            if (dateTime == null || dateTime == DateTime.MinValue)
            {
                return "N/A"; 
            }

            return dateTime.Value.ToString("HH:mm");
        }
        [HttpGet]
        [ActionName("BiometricHistory")]
        public VivifyResponse<List<VwBiometric>> BiometricHistory(DateOnly? fromDate,DateOnly? toDate)
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                List<VwBiometric> lst = new List<VwBiometric>();

                if (fromDate != null && toDate != null)
                {                    
                    lst = HRMSDBContext.VwBiometrics.Where(t => t.EmpNo== Empno && t.AttendanceDate >= fromDate && t.AttendanceDate <= toDate && t.CompanyId== CompanyID).OrderByDescending(x => x.Id).ToList();
                }
                else if (fromDate != null)
                {
                    lst = HRMSDBContext.VwBiometrics.Where(t => t.EmpNo == Empno && t.AttendanceDate == fromDate && t.CompanyId== CompanyID).OrderByDescending(x => x.Id).ToList();
                }
                else
                {                    
                   lst = HRMSDBContext.VwBiometrics.Where(t => t.EmpNo == Empno && t.CompanyId == CompanyID).OrderByDescending(x => x.Id).ToList();
                }

                return new VivifyResponse<List<VwBiometric>> { StatusCode = 200, StatusDesc = "Success", Result = lst };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<VwBiometric>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }

        [HttpGet]
        [ActionName("AdminBiometricReport")]
        public VivifyResponse<List<AdminBiometricReport>> AdminBiometricReport(
      string? BranchCode, long? EmpNo, DateOnly? fromDate, DateOnly? toDate)
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                // Prepare values or NULL for SQL string
                string branchParam = string.IsNullOrEmpty(BranchCode) ? "NULL" : $"'{BranchCode}'";
                string empNoParam = EmpNo.HasValue ? EmpNo.Value.ToString() : "NULL";
                string fromDateParam = fromDate.HasValue ? $"'{fromDate.Value}'" : "NULL";
                string toDateParam = toDate.HasValue ? $"'{toDate.Value}'" : "NULL";

                string sqlQuery = $"exec AdminBiometricReport {CompanyID}, {branchParam}, {empNoParam}, {fromDateParam}, {toDateParam}";

                List<AdminBiometricReport> lst = HRMSCustomDBContext.CAdminBiometricReport
                    .FromSqlRaw(sqlQuery)
                    .ToList();

                return new VivifyResponse<List<AdminBiometricReport>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = lst
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<AdminBiometricReport>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("AdminAttendanceReport")]
        public VivifyResponse<List<AttendanceReport>> AdminAttendanceReport(string BranchCode,long EmpNo, string? fromDate, string? toDate)
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                List<AttendanceReport> lst = new List<AttendanceReport>();          
                
                lst = HRMSCustomDBContext.CAttendanceReport.FromSqlRaw("exec AdminAttendanceReport "+CompanyID+",'" + BranchCode + "'," + EmpNo + ",'" + fromDate + "','" + toDate + "'").ToList();

                return new VivifyResponse<List<AttendanceReport>> { StatusCode = 200, StatusDesc = "Success", Result = lst };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<AttendanceReport>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
        }
        [HttpPost]
        [ActionName("UpdateApproveCmt")]
        public VivifyResponse<bool> UpdateApproveCmt([FromBody] MissPunch requestData)
        {
            try
            {
                long employeeNumber = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

                if (string.IsNullOrEmpty(requestData.ApproveCmt))
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 400,
                        StatusDesc = "Approval comment cannot be empty.",
                        Result = false
                    };
                }

                var missPunchRecord = HRMSDBContext.MissPunches
                    .FirstOrDefault(mp => mp.EmpNo == requestData.EmpNo &&
                                          mp.ReqDate.Date == requestData.ReqDate.Date &&
                                          mp.CompanyId == companyId);

                if (missPunchRecord == null)
                {
                    return new VivifyResponse<bool>
                    {
                        StatusCode = 404,
                        StatusDesc = "Miss Punch record not found.",
                        Result = false
                    };
                }

             
                missPunchRecord.ApproveCmt = requestData.ApproveCmt;
                missPunchRecord.Status = "2";  
                missPunchRecord.Upt_By = employeeNumber;

               
                bool biometricUpdated = UpdateBiometrics(employeeNumber, companyId, missPunchRecord);

                HRMSDBContext.SaveChanges();

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = biometricUpdated
                        ? "Miss Punch request approved."
                        : "Approval comment and status updated successfully, but no biometric changes were made.",
                    Result = true
                };
            }
            catch (Exception exception)
            {
                var innerExceptionMessage = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {exception.Message}. Inner Exception: {innerExceptionMessage}",
                    Result = false
                };
            }
        }
        private bool UpdateBiometrics(long employeeNumber, int companyId, MissPunch missPunchRecord)
        {
            bool biometricUpdated = false;
            var businessDate = DateOnly.FromDateTime(missPunchRecord.ReqDate);

            var checkInRecord = HRMSDBContext.EmpBiometrics
                .FirstOrDefault(b => b.EmpNo == missPunchRecord.EmpNo &&
                                     b.BusinessDate == businessDate &&
                                     b.CheckInType == 1 &&
                                     b.CompanyId == companyId);

            var checkOutRecord = HRMSDBContext.EmpBiometrics
                .FirstOrDefault(b => b.EmpNo == missPunchRecord.EmpNo &&
                                     b.BusinessDate == businessDate &&
                                     b.CheckInType == 2 &&
                                     b.CompanyId == companyId);

            if (checkInRecord != null && checkOutRecord == null)
            {
                var newCheckOut = new EmpBiometric
                {
                    EmpNo = missPunchRecord.EmpNo,
                    CompanyId = companyId,
                    BusinessDate = businessDate,
                    CheckInTime = missPunchRecord.PunchOutTime,
                    CheckInType = 2
                };
                HRMSDBContext.EmpBiometrics.Add(newCheckOut);

                newCheckOut.ShiftHours = checkInRecord.ShiftHours;
                biometricUpdated = true;
            }
            else if (checkOutRecord != null && checkInRecord == null)
            {
                var newCheckIn = new EmpBiometric
                {
                    EmpNo = missPunchRecord.EmpNo,
                    CompanyId = companyId,
                    BusinessDate = businessDate,
                    CheckInTime = missPunchRecord.PunchInTime,
                    CheckInType = 1
                };
                HRMSDBContext.EmpBiometrics.Add(newCheckIn);

                newCheckIn.ShiftHours = checkOutRecord.ShiftHours;
                biometricUpdated = true;
            }
            else if (checkInRecord != null && checkOutRecord != null)
            {
                checkInRecord.CheckInTime = missPunchRecord.PunchInTime;
                checkOutRecord.CheckInTime = missPunchRecord.PunchOutTime;

                if (checkInRecord.CheckInTime != null && checkOutRecord.CheckInTime != null)
                {
                    TimeSpan? workDuration = checkOutRecord.CheckInTime - checkInRecord.CheckInTime;
                    if (workDuration.HasValue)
                    {
                        checkInRecord.ShiftHours = (double?)workDuration.Value.TotalHours; 
                        biometricUpdated = true;
                    }
                }
            }
            else
            {
                if (missPunchRecord.PunchInTime != null)
                {
                    var newCheckIn = new EmpBiometric
                    {
                        EmpNo = missPunchRecord.EmpNo,
                        CompanyId = companyId,
                        BusinessDate = businessDate,
                        CheckInTime = missPunchRecord.PunchInTime,
                        CheckInType = 1
                    };
                    HRMSDBContext.EmpBiometrics.Add(newCheckIn);
                    biometricUpdated = true;
                }

                if (missPunchRecord.PunchOutTime != null)
                {
                    var newCheckOut = new EmpBiometric
                    {
                        EmpNo = missPunchRecord.EmpNo,
                        CompanyId = companyId,
                        BusinessDate = businessDate,
                        CheckInTime = missPunchRecord.PunchOutTime,
                        CheckInType = 2
                    };
                    HRMSDBContext.EmpBiometrics.Add(newCheckOut);
                    biometricUpdated = true;
                }
            }

            if (biometricUpdated)
            {
                HRMSDBContext.SaveChanges();
            }

            return biometricUpdated;
        }

        [HttpPost]
        [ActionName("RejectMissPunchRequest")]
        public VivifyResponse<string> RejectMissPunchRequest([FromBody] RejectMissPunchRequestDto request)
        {
            try
            {
                long authenticatedEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var missPunchRecord = HRMSDBContext.MissPunches
                    .FirstOrDefault(mp => mp.EmpNo == request.EmpNo
                                       && mp.ReqDate.Date == request.ReqDate.Date
                                       && mp.CompanyId == companyId);

                if (missPunchRecord == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "Miss Punch request not found."
                    };
                }

               
                missPunchRecord.Status = "0";
                missPunchRecord.ApproveCmt = request.ApproveCmt;  
                missPunchRecord.Upt_Date = DateTime.Now;
                missPunchRecord.Upt_By = authenticatedEmpNo;

                HRMSDBContext.SaveChanges();

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Miss Punch request rejected.",
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
        [ActionName("AddMissPunchRequest")]
        public VivifyResponse<bool> AddMissPunchRequest([FromBody] MissPunch requestData)
        {
            try
            {
               
                long employeeNumber = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(claim => claim.Type == "CompanyID")?.Value);

                
                var employeeInfo = CommonDBContext.EmployeeInfos
                    .Where(employee => employee.EmpNo == employeeNumber && employee.CompanyId == companyId)
                    .Select(employee => new { employee.BranchCode })
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

              
                var missPunchRecord = new MissPunch
                {
                    CompanyId = companyId,
                    EmpNo = employeeNumber,
                    ReqDate = requestData.ReqDate,
                    PunchInTime = requestData.PunchInTime,
                    PunchOutTime = requestData.PunchOutTime,
                    Remarks = requestData.Remarks,
                    Status = "1", 
                    BranchId = branchCode,
                    Crt_Date = DateTime.Now,
                    Crt_By = employeeNumber,
                    Upt_Date = DateTime.Now,
                    Upt_By = employeeNumber
                };

                
                HRMSDBContext.MissPunches.Add(missPunchRecord);
                HRMSDBContext.SaveChanges();

                return new VivifyResponse<bool>
                {
                    StatusCode = 200,
                    StatusDesc = "Miss Punch request successfully submitted.",
                    Result = true
                };
            }
            catch (Exception exception)
            {
                var innerExceptionMessage = exception.InnerException != null ? exception.InnerException.Message : exception.Message;
                return new VivifyResponse<bool>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {exception.Message}. Inner Exception: {innerExceptionMessage}",
                    Result = false
                };
            }
        }
        [HttpGet]
        [ActionName("GetMonthlyPunchCounts")]
        public async Task<VivifyResponse<List<AdminBiometricCountReport>>> GetMonthlyPunchCounts(
     string? BranchCode,
     long? EmpNo,
     DateOnly? fromDate,
     DateOnly? toDate)
        {
            try
            {
                long currentEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                DateOnly fromDateValue = fromDate ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));
                DateOnly toDateValue = toDate ?? DateOnly.FromDateTime(DateTime.Today);

             
                var employees = await CommonDBContext.EmployeeInfos
                    .Select(e => new { e.EmpNo, e.BranchCode })
                    .ToListAsync();

             
                var biometricData = await HRMSDBContext.EmpBiometrics
                    .Where(b => b.CompanyId == companyId &&
                                b.BusinessDate >= fromDateValue &&
                                b.BusinessDate <= toDateValue &&
                                (!EmpNo.HasValue || b.EmpNo == EmpNo.Value))
                    .ToListAsync();

            
                var query = from bio in biometricData
                            join emp in employees on bio.EmpNo equals emp.EmpNo
                            where string.IsNullOrEmpty(BranchCode) || emp.BranchCode == BranchCode
                            select new { bio, emp };

             
                var data = query
                    .GroupBy(x => new { x.bio.EmpNo, x.bio.BusinessDate })
                    .Select(g => new
                    {
                        g.Key.EmpNo,
                        g.Key.BusinessDate,
                        HasPunchIn = g.Any(x => x.bio.CheckInType == 1),
                        HasPunchOut = g.Any(x => x.bio.CheckInType == 2),
                        PunchInCount = g.Count(x => x.bio.CheckInType == 1),
                        PunchOutCount = g.Count(x => x.bio.CheckInType == 2)
                    })
                    .GroupBy(x => new { x.EmpNo, x.BusinessDate.Year, x.BusinessDate.Month })
                    .Select(g => new AdminBiometricCountReport
                    {
                        EmpNo = g.Key.EmpNo,
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        PunchInCount = g.Sum(x => x.PunchInCount),
                        PunchOutCount = g.Sum(x => x.PunchOutCount),
                        BothPunchInOutCount = g.Count(x => x.HasPunchIn && x.HasPunchOut)
                    })
                    .OrderBy(r => r.EmpNo)
                    .ThenBy(r => r.Year)
                    .ThenBy(r => r.Month)
                    .ToList();

                return new VivifyResponse<List<AdminBiometricCountReport>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = data
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<AdminBiometricCountReport>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("AdminIsEligibleReport")]
        public VivifyResponse<List<AdminIsEligibleReport>> AdminIsEligibleReport(
         string? BranchCode,
         long? EmpNo,
         DateOnly? fromDate,
         DateOnly? toDate,
         int? isEligOutSidePunch // 1 or 0 expected
     )
        {
            try
            {
                long currentEmpno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var empList = CommonDBContext.EmployeeInfos
                    .Where(e => e.CompanyId == companyId)
                    .ToList();

                var branchList = CommonDBContext.MBranches
                    .Where(b => b.CompanyId == companyId)
                    .ToList();

                var checkIns = HRMSDBContext.EmpBiometrics
                    .Where(x => x.CompanyId == companyId && x.CheckInType == 1)
                    .GroupBy(x => new { x.EmpNo, x.BusinessDate })
                    .Select(g => new
                    {
                        g.Key.EmpNo,
                        g.Key.BusinessDate,
                        CheckInTime = g.Min(x => x.CheckInTime)
                    })
                    .ToList();

                var checkOuts = HRMSDBContext.EmpBiometrics
                    .Where(x => x.CompanyId == companyId && x.CheckInType == 2)
                    .GroupBy(x => new { x.EmpNo, x.BusinessDate })
                    .Select(g => new
                    {
                        g.Key.EmpNo,
                        g.Key.BusinessDate,
                        CheckOutTime = g.Max(x => x.CheckInTime)
                    })
                    .ToList();

                var empExceptions = HRMSDBContext.EmpBioExceptions
                    .Where(e => e.CompanyId == companyId)
                    .GroupBy(e => e.EmpNo)
                    .Select(g => g.OrderByDescending(x => x.Id).First())
                    .ToList();

                var report = (from emp in empList
                              join branch in branchList on emp.BranchCode equals branch.BranchCode into branchJoin
                              from branch in branchJoin.DefaultIfEmpty()
                              join ci in checkIns on emp.EmpNo equals ci.EmpNo
                              join co in checkOuts on new { ci.EmpNo, ci.BusinessDate } equals new { co.EmpNo, co.BusinessDate } into coJoin
                              from co in coJoin.DefaultIfEmpty()
                              let exception = empExceptions.FirstOrDefault(e => e.EmpNo == emp.EmpNo)
                              select new AdminIsEligibleReport
                              {
                                  BranchCode = emp.BranchCode,
                                  BranchDesc = branch?.BranchDesc,
                                  EmpNo = emp.EmpNo,
                                  Name = emp.FirstName + " " + emp.LastName,
                                  AttendanceDate = ci.BusinessDate.ToDateTime(new TimeOnly()),
                                  CheckInTime = ci.CheckInTime,
                                  CheckOutTime = co?.CheckOutTime,
                                  IsEligOutSidePunch = exception?.IsEligOutSidePunch
                              }).ToList();

                // Apply filters
                if (!string.IsNullOrEmpty(BranchCode))
                    report = report.Where(x => x.BranchCode == BranchCode).ToList();

                if (EmpNo.HasValue)
                    report = report.Where(x => x.EmpNo == EmpNo).ToList();

                if (fromDate.HasValue)
                    report = report.Where(x => x.AttendanceDate >= fromDate.Value.ToDateTime(new TimeOnly())).ToList();

                if (toDate.HasValue)
                    report = report.Where(x => x.AttendanceDate <= toDate.Value.ToDateTime(new TimeOnly())).ToList();

                if (isEligOutSidePunch.HasValue)
                {
                    bool elig = isEligOutSidePunch == 1;
                    report = report.Where(x => x.IsEligOutSidePunch == elig).ToList();
                }

                return new VivifyResponse<List<AdminIsEligibleReport>>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = report
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<AdminIsEligibleReport>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = null
                };
            }
        }



    }
}
