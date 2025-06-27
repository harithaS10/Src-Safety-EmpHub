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


namespace SafetyToolBoxAPI.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    [Authorize]
    public class BiometricController : ControllerBase
    {
        private readonly VivifyHrmsContext HRMSDBContext;
        private readonly CustomDBHRMSContext HRMSCustomDBContext;
        private IConfiguration Config;

        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public BiometricController(VivifyHrmsContext _HRMSDBContext, IConfiguration _config, CustomDBHRMSContext _CustomDBHRMSContext)
        {
            HRMSDBContext = _HRMSDBContext;
            HRMSCustomDBContext = _CustomDBHRMSContext;
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
                bool ret = Geo.checkPointExistsInGeofencePolygon("80.1764079,13.0958506|80.1764263,13.0958212|80.1764803,13.0958173|80.1764937,13.095863|80.1764314,13.0958786|80.1764079,13.0958506", ObjBiometric.Long, ObjBiometric.Lat);
                if(ret==false)
                {
                  //  throw new Exception("CheckIn and CheckOut should be with in office premise");
                }
                DateOnly dt = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE));
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                string? RefCol = Guid.NewGuid().ToString();

                if (ObjBiometric.TypeOfCheckIn == 1)
                {
                    var Verification = HRMSDBContext.EmpBiometrics.Where(x => x.EmpNo == Empno && x.BusinessDate == dt && x.CompanyId == CompanyID).OrderByDescending(x => x.Id).FirstOrDefault();
                    if (Verification != null && Verification.CheckInType==1)
                    {   
                        return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Already checkIn done. Please do checkout then checkIn", Result = true };
                    }
                }
                if (ObjBiometric.TypeOfCheckIn == 2)
                {
                    var Verification = HRMSDBContext.EmpBiometrics.Where(x => x.EmpNo == Empno && x.BusinessDate == dt && x.CompanyId == CompanyID).OrderByDescending(x => x.Id).FirstOrDefault();
                    if (Verification == null || Verification.CheckInType == 2)
                    {                        
                        return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "There is no check in available. Please do checkin then checkout", Result = true };
                    }
                    else
                    {
                        RefCol = Verification.CheckInRef;
                    }
                }

                EmpBiometric objEmpBiometric = new EmpBiometric();
                objEmpBiometric.EmpNo = Empno;
                objEmpBiometric.CompanyId = CompanyID;
                objEmpBiometric.BusinessDate = dt;
                objEmpBiometric.CheckInTime =TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE);
                objEmpBiometric.CheckInType = ObjBiometric.TypeOfCheckIn;
                objEmpBiometric.CheckInRef = RefCol;
                HRMSDBContext.EmpBiometrics.Add(objEmpBiometric);
                HRMSDBContext.SaveChanges();
                if (ObjBiometric.TypeOfCheckIn == 1)
                {
                    return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Successfully marked CheckIn", Result = true };
                }
                else
                {
                    return new VivifyResponse<bool> { StatusCode = 200, StatusDesc = "Successfully marked CheckOut", Result = true };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<bool> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
            }
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
        public VivifyResponse<List<AdminBiometricReport>> AdminBiometricReport(string BranchCode,long EmpNo, DateOnly? fromDate, DateOnly? toDate)
        {
            try
            {
                long Empno = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                List<AdminBiometricReport> lst = new List<AdminBiometricReport>();                

                lst = HRMSCustomDBContext.CAdminBiometricReport.FromSqlRaw("exec AdminBiometricReport "+ CompanyID+",'"+ BranchCode+"'," + EmpNo + ",'" + fromDate + "','" + toDate + "'").ToList();


                return new VivifyResponse<List<AdminBiometricReport>> { StatusCode = 200, StatusDesc = "Success", Result = lst };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<AdminBiometricReport>> { StatusCode = 500, StatusDesc = ex.Message + ":" + ex.StackTrace };
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

    }
}
