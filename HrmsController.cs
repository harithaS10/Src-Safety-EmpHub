using System.Collections.Generic;
using System.Net;
using System.Reflection.Metadata;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using MediaToolkit;
using MediaToolkit.Model;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SafetyToolBoxAPI.Common;
using SafetyToolBoxDL.CommonDB;
using SafetyToolBoxDL.CustomDB;
using SafetyToolBoxDL.HRMSDB;
using SafetyToolBoxDL.SafetyDB;
using static SafetyToolBoxAPI.Common.BranchInfo;
using static SafetyToolBoxAPI.Common.RejectMissPunchRequestDto;
using System.Net.Mail;
using System.Linq;
using Xabe.FFmpeg;
using System.ComponentModel.Design;
using SafetyToolBoxAPI.CommonDB;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
namespace SafetyToolBoxAPI.Controllers
{
    [Route("api/[controller]/[Action]")]
    [ApiController]
    [Authorize]
    public class Hrms : ControllerBase
    {
        private readonly VivifyHrmsContext HRMSDBContext;
        private readonly CustomDBHRMSContext HRMSCustomDBContext;
        private readonly VivifyCommonContext CommonDBContext;
        private IConfiguration Config;

        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        public Hrms(VivifyHrmsContext _HRMSDBContext, IConfiguration _config, CustomDBHRMSContext _CustomDBHRMSContext, VivifyCommonContext _commonDBContext)
        {
            HRMSDBContext = _HRMSDBContext;
            HRMSCustomDBContext = _CustomDBHRMSContext;
            CommonDBContext = _commonDBContext;
            Config = _config;
        }
        [HttpGet]
        [ActionName("CreditMonthlyLeave")]
        public VivifyResponse<string> CreditMonthlyLeave()
        {
            try
            {
               
                CommonDBContext.Database.ExecuteSqlRaw("EXEC [dbo].[InsertMonthlyLeaveBalanceForAllEmployees]");

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Monthly leave balances credited successfully.",
                    Result = "Success"
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error crediting monthly leave: {ex.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("CreditYearlyLeave")]
        public VivifyResponse<string> CreditYearlyLeave()
        {
            try
            {
                // Execute the stored procedure using the existing EF Core context
                CommonDBContext.Database.ExecuteSqlRaw("EXEC [dbo].[InsertYearlyLeaveBalanceForAllEmployees]");

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Yearly leave balances credited successfully.",
                    Result = "Success"
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error crediting yearly leave: {ex.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddEmployeeWorkExperience")]
        public async Task<VivifyResponse<object>> AddEmployeeWorkExperience([FromForm] WorkExperienceRequest request)
        {
            try
            {
                
                if (request.EmpNo <= 0 || string.IsNullOrEmpty(request.OrgName) ||
                    string.IsNullOrEmpty(request.Designation) || request.MonthOfExp <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid request. EmpNo, OrgName, Designation, and MonthOfExp are required.",
                        Result = null
                    };
                }

              
                var experience = new EmployeeWorkExperience
                {
                    EmpId = request.EmpNo,
                    OrgName = request.OrgName,
                    Designation = request.Designation,
                    Joindate = request.JoinDate,
                    ReleivingDate = request.ReleivingDate,
                    MonthOfExp = request.MonthOfExp,
                    Skills = request.Skills
                };

                CommonDBContext.EmployeeWorkExperiences.Add(experience);
                await CommonDBContext.SaveChangesAsync(); 

              

                if (request.Payslip != null)
                {
                    var payslipPath = await SaveAttachmentFileAsync(request.Payslip);
                    CommonDBContext.EmployeeWorkDocuments.Add(new EmployeeWorkDocument
                    {
                        ExpId = experience.RowId,
                        EmpId = request.EmpNo,
                        DocumentTypeId = 6,
                        DocumentPath = payslipPath,
                        UpdatedBy = request.EmpNo.ToString()
                    });
                }

                if (request.ExperienceLetter != null)
                {
                    var expLetterPath = await SaveAttachmentFileAsync(request.ExperienceLetter);
                    CommonDBContext.EmployeeWorkDocuments.Add(new EmployeeWorkDocument
                    {
                        ExpId = experience.RowId,
                        EmpId = request.EmpNo,
                        DocumentTypeId = 7,
                        DocumentPath = expLetterPath,
                        UpdatedBy = request.EmpNo.ToString()
                    });
                }

             
                if (request.OfferLetter != null)
                {
                    var offerLetterPath = await SaveAttachmentFileAsync(request.OfferLetter);
                    CommonDBContext.EmployeeWorkDocuments.Add(new EmployeeWorkDocument
                    {
                        ExpId = experience.RowId,
                        EmpId = request.EmpNo,
                        DocumentTypeId = 14, 
                        DocumentPath = offerLetterPath,
                        UpdatedBy = request.EmpNo.ToString()
                    });
                }

                await CommonDBContext.SaveChangesAsync(); 

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee work experience and documents added successfully.",
                    Result = new { EmpNo = request.EmpNo }
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmployeeWorkExperience")]
        [Authorize]
        public VivifyResponse<object> GetEmployeeWorkExperience([FromQuery] long? empNo)
        {
            try
            {
                IQueryable<EmployeeWorkExperience> query = CommonDBContext.EmployeeWorkExperiences;

              
                if (empNo.HasValue && empNo > 0)
                {
                    query = query.Where(w => w.EmpId == empNo.Value);
                }

                var experienceRecords = query.ToList();

                if (!experienceRecords.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No work experience records found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Work experience records retrieved successfully.",
                    Result = experienceRecords
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }


        public static class EducationMappingHelper
        {
           
            public static readonly Dictionary<int, int> EducationDocumentTypeMapping = new()
    {
        { 1, 1 },
        { 3, 2 }, 
        { 4, 3 },
        { 5, 4 }, 
        { 6, 5 }  
    };
        }
        [HttpPost]
        [ActionName("AddEmployeeEducation")]
        public async Task<VivifyResponse<object>> AddEmployeeEducation([FromForm] EducationRequest request, [FromForm] int EmpNo)
        {
            try
            {
              
                if (request.EducationId <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid Education ID. Please provide a valid Education ID.",
                        Result = null
                    };
                }

             
                Console.WriteLine($"Submitted EducationID: {request.EducationId}");

             
                if (!EducationMappingHelper.EducationDocumentTypeMapping.TryGetValue(request.EducationId, out int documentTypeId))
                {
                    Console.WriteLine($"No mapping found for EducationID: {request.EducationId}");
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = $"Unable to determine Document Type ID for the given Education ID ({request.EducationId}). Please contact support.",
                        Result = null
                    };
                }

              
                var educationRecord = CommonDBContext.EmployeeEducations
                    .FirstOrDefault(e => e.EmpId == EmpNo && e.EducationId == request.EducationId);

                bool isUpdate = false;

                if (educationRecord == null)
                {
                    educationRecord = new EmployeeEducation
                    {
                        EmpId = EmpNo,
                        EducationId = request.EducationId,
                        SchoolName = request.InstitutionName,
                        FieldOfStudy = request.FieldOfStudy,
                        StartDate = request.StartDate,
                        EndDate = request.EndDate,
                        GradeAverage = request.GradeOrCGPA,
                        Location = request.Location,
                        DateDegreeReceived = request.DateOfDegreeReceived,
                        YearCompleted = request.YearCompleted,
                        Graduate = request.Graduate,
                        UpdatedDate = DateOnly.FromDateTime(DateTime.Now),
                        DocumentTypeId = documentTypeId, 
                        IsActive = true 
                    };
                    CommonDBContext.EmployeeEducations.Add(educationRecord);
                }
                else
                {
                  
                    educationRecord.SchoolName = request.InstitutionName;
                    educationRecord.FieldOfStudy = request.FieldOfStudy;
                    educationRecord.StartDate = request.StartDate;
                    educationRecord.EndDate = request.EndDate;
                    educationRecord.GradeAverage = request.GradeOrCGPA;
                    educationRecord.Location = request.Location;
                    educationRecord.DateDegreeReceived = request.DateOfDegreeReceived;
                    educationRecord.YearCompleted = request.YearCompleted;
                    educationRecord.Graduate = request.Graduate;
                    educationRecord.UpdatedDate = DateOnly.FromDateTime(DateTime.Now); 
                    educationRecord.DocumentTypeId = documentTypeId; 
                    educationRecord.IsActive = true;

                    CommonDBContext.EmployeeEducations.Update(educationRecord);
                    isUpdate = true; 
                }

                await CommonDBContext.SaveChangesAsync();

              
                if (request.DocumentFile != null && request.DocumentFile.Length > 0)
                {
                    var documentPath = await SaveAttachmentFileAsync(request.DocumentFile);

                 
                    var existingDocument = CommonDBContext.EmployeeDocuments
                        .FirstOrDefault(d => d.EmpId == EmpNo && d.DocumentTypeId == documentTypeId);

                    if (existingDocument == null)
                    {
                     
                        var employeeDocument = new EmployeeDocument
                        {
                            EmpId = EmpNo,
                            DocumentName = Path.GetFileName(request.DocumentFile.FileName),
                            DocumentLocation = documentPath,
                            DocumentTypeId = documentTypeId,
                            DateUpdated = DateOnly.FromDateTime(DateTime.Now),
                            OrignalDateUploaded = DateOnly.FromDateTime(DateTime.Now)
                        };

                        CommonDBContext.EmployeeDocuments.Add(employeeDocument);
                    }
                    else
                    {
                     
                        existingDocument.DocumentName = Path.GetFileName(request.DocumentFile.FileName);
                        existingDocument.DocumentLocation = documentPath;
                        existingDocument.DateUpdated = DateOnly.FromDateTime(DateTime.Now);

                        CommonDBContext.EmployeeDocuments.Update(existingDocument);
                    }

                    await CommonDBContext.SaveChangesAsync();
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = isUpdate ? "Education details updated successfully." : "Education details added successfully.",
                    Result = new { EmpNo = EmpNo }
                };
            }
            catch (Exception ex)
            {
              
                Console.WriteLine($"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}");

               
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeEducation")]
        [Authorize] 
        public VivifyResponse<object> GetEmployeeEducation([FromQuery] long? empNo)
        {
            try
            {
               
                var educationQuery = from edu in CommonDBContext.EmployeeEducations
                                     join type in CommonDBContext.EducationTypes
                                         on edu.EducationId equals type.Id
                                     join field in CommonDBContext.FieldOfStudies
                                         on edu.FieldOfStudy equals field.Id into fieldJoin
                                     from field in fieldJoin.DefaultIfEmpty()
                                     where edu.IsActive == true 
                                     select new
                                     {
                                         edu.RowId,
                                         edu.EmpId,
                                         edu.EducationId,
                                         edu.DocumentTypeId, 
                                         EducationName = type.EducationDesc,
                                         edu.SchoolName,
                                         FieldOfStudyId = edu.FieldOfStudy,
                                         FieldOfStudyName = field != null ? field.FieldName : null,
                                         edu.StartDate,
                                         edu.EndDate,
                                         edu.DateDegreeReceived,
                                         edu.YearCompleted,
                                         edu.Graduate,
                                         edu.GradeAverage,
                                         edu.Location,
                                         edu.IsActive 
                                     };

             
                if (empNo.HasValue && empNo > 0)
                {
                    educationQuery = educationQuery.Where(e => e.EmpId == empNo.Value);
                }

              
                var educationRecords = educationQuery.ToList();

                if (!educationRecords.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active education records found.",
                        Result = null
                    };
                }

               
                var educationWithDocuments = educationRecords.Select(edu => new
                {
                    edu.RowId,
                    edu.EmpId,
                    edu.EducationId,
                    edu.DocumentTypeId, 
                    edu.EducationName,
                    edu.SchoolName,
                    edu.FieldOfStudyId,
                    edu.FieldOfStudyName,
                    edu.StartDate,
                    edu.EndDate,
                    edu.DateDegreeReceived,
                    edu.YearCompleted,
                    edu.Graduate,
                    edu.GradeAverage,
                    edu.Location,
                    edu.IsActive, 
                    Documents = CommonDBContext.EmployeeDocuments
                        .Where(doc => doc.EmpId == edu.EmpId && doc.DocumentTypeId == edu.DocumentTypeId ) // Filter only active documents
                        .Select(doc => new
                        {
                            doc.DocumentTypeId,
                            doc.DocumentLocation,
                           
                        }).ToList()
                }).ToList();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Active education records and documents retrieved successfully.",
                    Result = educationWithDocuments
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("DeleteEmployeeEducation")]
        [Authorize]
        public async Task<VivifyResponse<object>> DeleteEmployeeEducation([FromQuery] long empNo, [FromQuery] int educationId)
        {
            try
            {
                
                var education = await CommonDBContext.EmployeeEducations
                    .FirstOrDefaultAsync(e => e.EmpId == empNo && e.EducationId == educationId);

              
                if (education == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Education record not found.",
                        Result = null
                    };
                }

             
                bool isActive = education.IsActive.GetValueOrDefault(false);

                if (!isActive)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Education record already deleted.",
                        Result = null
                    };
                }

                education.IsActive = false;
                education.UpdatedDate = DateOnly.FromDateTime(DateTime.Now);

                CommonDBContext.EmployeeEducations.Update(education);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Education record deleted successfully.",
                    Result = null
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("DeleteEmployeeDependent")]
        [Authorize]
        public async Task<VivifyResponse<object>> DeleteEmployeeDependent([FromQuery] long sponsorEmpId, [FromQuery] long dependentId)
        {
            try
            {
                var dependent = await CommonDBContext.EmployeeDependents
                    .FirstOrDefaultAsync(d => d.SponsorEmpId == sponsorEmpId && d.DependentId == dependentId);

                if (dependent == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Dependent record not found.",
                        Result = null
                    };
                }

                bool isActive = dependent.IsActive.GetValueOrDefault(false);

                if (!isActive)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Dependent record already deleted.",
                        Result = null
                    };
                }

                dependent.IsActive = false;
                dependent.UpdatedDate = DateOnly.FromDateTime(DateTime.Now);

                CommonDBContext.EmployeeDependents.Update(dependent);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Dependent record deleted successfully.",
                    Result = null
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }



        [HttpGet]
        [ActionName("EditEducationDetails")]
        public VivifyResponse<object> EditEducationDetails(long educationId, long empNo)
        {
            try
            {
                
                if (educationId <= 0 || empNo <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid EducationId or EmpNo.",
                        Result = null
                    };
                }

               
                var educationRecord = (
                    from edu in CommonDBContext.EmployeeEducations
                    join type in CommonDBContext.EducationTypes
                        on edu.EducationId equals type.Id
                    join field in CommonDBContext.FieldOfStudies
                        on edu.FieldOfStudy equals field.Id into fieldJoin
                    from field in fieldJoin.DefaultIfEmpty()
                    join doc in CommonDBContext.EmployeeDocuments
                        on new { EmpId = (long)edu.EmpId, DocumentTypeID = (int)edu.DocumentTypeId }
                        equals new { EmpId = (long)doc.EmpId, DocumentTypeID = (int)doc.DocumentTypeId } into docJoin
                    from doc in docJoin.DefaultIfEmpty() 
                    where edu.EmpId == empNo && edu.EducationId == educationId
                    orderby edu.UpdatedDate descending 
                    select new
                    {
                        edu.RowId,
                        edu.EmpId,
                        edu.EducationId,
                        EducationName = type.EducationDesc,
                        edu.SchoolName,
                        FieldOfStudyId = edu.FieldOfStudy,
                        FieldOfStudyName = field != null ? field.FieldName : null,
                        edu.StartDate,
                        edu.EndDate,
                        edu.DateDegreeReceived,
                        edu.YearCompleted,
                        edu.Graduate,
                        edu.GradeAverage,
                        edu.Location,
                        edu.UpdatedDate, 
                        DocumentLocation = doc != null ? doc.DocumentLocation : null 
                    }
                ).FirstOrDefault(); 

               
                if (educationRecord == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No education record found for the given EducationId and EmpNo.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Education record retrieved successfully.",
                    Result = educationRecord
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetEducationTypes")]
        public VivifyResponse<List<EducationTypeResponse>> GetEducationTypes()
        {
            try
            {
                var educationTypes = CommonDBContext.EducationTypes
                    .Where(x => x.Status == true) 
                    .Select(x => new EducationTypeResponse
                    {
                       EducationId = x.Id,
                        EducationDesc = x.EducationDesc,
                        ShortCode = x.ShortCode,
                        Status = x.Status
                    })
                    .ToList();

                return new VivifyResponse<List<EducationTypeResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Education types fetched successfully.",
                    Result = educationTypes
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<EducationTypeResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<EducationTypeResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetFieldOfStudies")]
        public VivifyResponse<List<FieldOfStudyResponse>> GetFieldOfStudies([FromQuery] string fieldName = null)
        {
            try
            {
                var query = CommonDBContext.FieldOfStudies.AsQueryable();

              
                if (!string.IsNullOrEmpty(fieldName))
                {
                    query = query.Where(x => x.FieldName.Contains(fieldName)); 
                }

                var fields = query
                    .Select(x => new FieldOfStudyResponse
                    {
                        FieldId = x.Id,
                        FieldName = x.FieldName
                    })
                    .ToList();

                return new VivifyResponse<List<FieldOfStudyResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Field of study types fetched successfully.",
                    Result = fields
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<FieldOfStudyResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<FieldOfStudyResponse>()
                };
            }
        }

        [HttpGet]
        [ActionName("GetGenderTypes")]
        public VivifyResponse<List<GenderType>> GetGenderTypes()
        {
            try
            {
              
                var genderTypes = CommonDBContext.GenderTypes
                    .Select(x => new GenderType
                    {
                      Id = x.Id,
                        GenderName = x.GenderName
                    })
                    .ToList();

                return new VivifyResponse<List<GenderType>>
                {
                    StatusCode = 200,
                    StatusDesc = "Gender types fetched successfully.",
                    Result = genderTypes
                };
            }
            catch (Exception ex)
            {
               
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<GenderType>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<GenderType>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetLineManagers")]
        public VivifyResponse<List<LineManagerResponse>> GetLineManagers()
        {
            try
            {
                var managers = CommonDBContext.LineManagers
                    .Select(x => new LineManagerResponse
                    {
                        LineMgrID = x.LineMgrId,
                        LineManager = x.LineManager1,
                        EmployeeID = x.EmployeeID
                    })
                    .ToList();

                return new VivifyResponse<List<LineManagerResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Line managers fetched successfully.",
                    Result = managers
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<LineManagerResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<LineManagerResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetSkillLevels")]
        public VivifyResponse<List<SkillLevelResponse>> GetSkillLevels()
        {
            try
            {
                var skillLevels = CommonDBContext.SkillLevels
                    .Select(x => new SkillLevelResponse
                    {
                        SkillLevelId = x.Id,
                        SkillLevelName = x.Name,
                    })
                    .ToList();

                return new VivifyResponse<List<SkillLevelResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Skill levels fetched successfully.",
                    Result = skillLevels
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<SkillLevelResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<SkillLevelResponse>()
                };
            }
        }

        [HttpGet]
        [ActionName("GetRelationshipTypes")]
        public VivifyResponse<List<RelationshipTypeResponse>> GetRelationshipTypes()
        {
            try
            {
                var relationships = CommonDBContext.Relationships
                    .Select(x => new RelationshipTypeResponse
                    {
                        RelationshipID = x.RelationshipId,
                        RelationshipName = x.RelationshipName
                    })
                    .ToList();

                return new VivifyResponse<List<RelationshipTypeResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Relationship types fetched successfully.",
                    Result = relationships
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<RelationshipTypeResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<RelationshipTypeResponse>()
                };
            }
        }
       [HttpPost]
[ActionName("AddEmployeeDependent")]
public VivifyResponse<object> AddEmployeeDependent([FromForm] DependentRequest request)
{
    try
    {
      
        var relationshipExists = CommonDBContext.Relationships.Any(r => r.RelationshipId == request.RelationshipID);
        if (!relationshipExists)
        {
            return new VivifyResponse<object>
            {
                StatusCode = 400,
                StatusDesc = "Invalid RelationshipID provided."
            };
        }

      
        var genderExists = CommonDBContext.GenderTypes.Any(g => g.Id == request.GenderTypeID);
        if (!genderExists)
        {
            return new VivifyResponse<object>
            {
                StatusCode = 400,
                StatusDesc = "Invalid GenderTypeID provided."
            };
        }

       
        var dependent = CommonDBContext.EmployeeDependents
            .FirstOrDefault(d => d.SponsorEmpId == request.EmpNo && d.FullName == request.FullName);

        bool isUpdate = false;

        if (dependent != null)
        {
            
            dependent.RelationshipId = request.RelationshipID;
            dependent.Dob = request.DOB;
            dependent.GenderTypeId = request.GenderTypeID;
            dependent.UpdatedDate = DateOnly.FromDateTime(DateTime.Now);  
                    dependent.IsActive = true; 

            CommonDBContext.EmployeeDependents.Update(dependent);
            isUpdate = true;
        }
        else
        {
            dependent = new EmployeeDependent
            {
                FullName = request.FullName,
                RelationshipId = request.RelationshipID,
                Dob = request.DOB,
                GenderTypeId = request.GenderTypeID,
                SponsorEmpId = request.EmpNo,
                UpdatedDate = DateOnly.FromDateTime(DateTime.Now),
                IsActive = true
            };

            CommonDBContext.EmployeeDependents.Add(dependent);
        }

      
        CommonDBContext.SaveChanges();

       
        return new VivifyResponse<object>
        {
            StatusCode = 200,
            StatusDesc = isUpdate ? "Employee dependent updated successfully." : "Employee dependent added successfully.",
            Result = new { EmpNo = dependent.SponsorEmpId }
        };
    }
    catch (Exception ex)
    {
        return new VivifyResponse<object>
        {
            StatusCode = 500,
            StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
            Result = null
        };
    }
}

        [HttpGet]
        [ActionName("EditDependent")]
        public VivifyResponse<object> EditDependent(int empNo, int relationshipId)
        {
            try
            {
                
                var dependent = (
                    from d in CommonDBContext.EmployeeDependents
                    join g in CommonDBContext.GenderTypes on d.GenderTypeId equals g.Id
                    join r in CommonDBContext.Relationships on d.RelationshipId equals r.RelationshipId
                    where d.SponsorEmpId == empNo && d.RelationshipId == relationshipId
                    orderby d.UpdatedDate descending
                    select new
                    {
                        d.DependentId,
                        d.FullName,
                        d.RelationshipId,
                        RelationshipName = r.RelationshipName,
                        d.Dob,
                        d.GenderTypeId,
                        GenderName = g.GenderName,
                        d.SponsorEmpId,
                        d.UpdatedDate
                    }
                ).FirstOrDefault();

                if (dependent == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No dependents found for the given EmpNo and RelationshipId.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Dependent retrieved successfully.",
                    Result = dependent
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeDependents")]
        [Authorize]
        public VivifyResponse<object> GetEmployeeDependents([FromQuery] long? empNo)
        {
            try
            {
              
                var dependentsQuery = from d in CommonDBContext.EmployeeDependents
                                      join g in CommonDBContext.GenderTypes on d.GenderTypeId equals g.Id
                                      join r in CommonDBContext.Relationships on d.RelationshipId equals r.RelationshipId
                                      where d.IsActive == true
                                      select new
                                      {
                                          d.DependentId,
                                          d.FullName,
                                          d.RelationshipId,
                                          RelationshipName = r.RelationshipName,
                                          d.Dob,
                                      d.GenderTypeId,
                                          GenderName = g.GenderName,
                                          d.IsActive,
                                          d.SponsorEmpId
                                      };

               
                if (empNo.HasValue && empNo > 0)
                {
                    dependentsQuery = dependentsQuery.Where(d => d.SponsorEmpId == empNo.Value);
                }

                var dependents = dependentsQuery.ToList();

                if (!dependents.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No dependents found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Dependents retrieved successfully.",
                    Result = dependents
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }




        [HttpGet]
        [ActionName("GetAllLanguages")]
        public VivifyResponse<List<Language>> GetAllLanguages()
        {
            try
            {
                var languages = CommonDBContext.Languages.ToList();

                return new VivifyResponse<List<Language>>
                {
                    StatusCode = 200,
                    StatusDesc = "Languages fetched successfully.",
                    Result = languages
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<Language>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmployeeSkill")]
        public VivifyResponse<object> AddEmployeeSkill([FromForm] SkillRequest request)
        {
            try
            {
               
                var skillLevelExists = CommonDBContext.SkillLevels.Any(x => x.Id == request.SkillLevel_Id);
                if (!skillLevelExists)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid SkillLevel_Id.",
                        Result = null
                    };
                }

                
                var employeeSkill = new EmployeeSkill
                {
                    EmpId = request.EmpId,
                    SkillId = request.Skill_Id, 
                    Skill_Desc = request.Skill_Desc,
                    SkillLevelId = request.SkillLevel_Id,
                    Last_Assessed = request.Last_Assessed,
                    Note = request.Note
                };

                CommonDBContext.EmployeeSkills.Add(employeeSkill);
                CommonDBContext.SaveChanges();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee skill added successfully.",
                    Result = new { EmpId = request.EmpId }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmployeeSkill")]
        [Authorize] 
        public VivifyResponse<object> GetEmployeeSkill([FromQuery] long? empNo)
        {
            try
            {
                IQueryable<EmployeeSkill> query = CommonDBContext.EmployeeSkills;

             
                if (empNo.HasValue && empNo > 0)
                {
                    query = query.Where(skill => skill.EmpId == empNo.Value);
                }

                var skillRecords = (
                    from s in query
                    join level in CommonDBContext.SkillLevels
                        on s.SkillLevelId equals level.Id
                    select new
                    {
                        s.SkillId,
                        s.EmpId,
                        s.Skill_Desc,
                        s.SkillLevelId,
                        SkillLevelName = level.Name,
                        s.Last_Assessed,
                        s.Note
                    }
                ).ToList();

                if (!skillRecords.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No skill records found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee skill records retrieved successfully.",
                    Result = skillRecords
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddEmployeeLanguage")]
        public VivifyResponse<object> AddEmployeeLanguage([FromForm] EmployeeLanguageRequest request)
        {
            try
            {
               
                if (request.EmpId <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing EmpId.",
                        Result = null
                    };
                }

              
                if (request.ReadLevelId <= 0 || request.WriteLevelId <= 0 || request.SpeakLevelId <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid Skill Level IDs provided. All skill levels must have valid IDs.",
                        Result = null
                    };
                }

              
                var language = CommonDBContext.Languages.FirstOrDefault(l => l.LanguageId == request.LanguageId);
                if (language == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid LanguageId provided.",
                        Result = null
                    };
                }

              
                var existingRecord = CommonDBContext.EmployeeLanguages
                    .FirstOrDefault(el => el.EmpId == request.EmpId && el.LanguageId == request.LanguageId);

                if (existingRecord != null)
                {
                   
                    return new VivifyResponse<object>
                    {
                        StatusCode = 409, 
                        StatusDesc = "This language is already exists.",
                        Result = new { EmpId = request.EmpId, LanguageId = request.LanguageId }
                    };
                }

             
                var empLang = new EmployeeLanguage
                {
                    EmpId = request.EmpId,
                    LanguageId = request.LanguageId,
                    ReadLevelId = request.ReadLevelId,
                    WriteLevelId = request.WriteLevelId,
                    SpeakLevelId = request.SpeakLevelId,
                    LastAssessed = request.Last_Assessed
                };

                CommonDBContext.EmployeeLanguages.Add(empLang);
                CommonDBContext.SaveChanges();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee language added successfully.",
                    Result = new { EmpId = request.EmpId, LanguageId = request.LanguageId }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeLanguages")]
        public async Task<VivifyResponse<object>> GetEmployeeLanguages([FromQuery] long? empId)
        {
            try
            {
                IQueryable<EmployeeLanguageDTO> languageQuery = from empLang in CommonDBContext.EmployeeLanguages
                                                                join lang in CommonDBContext.Languages
                                                                    on empLang.LanguageId equals lang.LanguageId
                                                                select new EmployeeLanguageDTO
                                                                {
                                                                    EmpId = empLang.EmpId,
                                                                    LanguageName = lang.LanguageName,
                                                                    ReadLevelId = empLang.ReadLevelId,
                                                                    WriteLevelID = empLang.WriteLevelId,
                                                                    SpeakLevelId = empLang.SpeakLevelId,
                                                                    LastAssessed = empLang.LastAssessed,
                                                                    ReadLevelName = CommonDBContext.SkillLevels
                                                                        .Where(sl => sl.Id == empLang.ReadLevelId)
                                                                        .Select(sl => sl.Name)
                                                                        .FirstOrDefault(),
                                                                    WriteLevelName = CommonDBContext.SkillLevels
                                                                        .Where(sl => sl.Id == empLang.WriteLevelId)
                                                                        .Select(sl => sl.Name)
                                                                        .FirstOrDefault(),
                                                                    SpeakLevelName = CommonDBContext.SkillLevels
                                                                        .Where(sl => sl.Id == empLang.SpeakLevelId)
                                                                        .Select(sl => sl.Name)
                                                                        .FirstOrDefault()
                                                                };

               
                if (empId.HasValue && empId > 0)
                {
                    languageQuery = languageQuery.Where(e => e.EmpId == empId.Value);
                }

                var languages = await languageQuery.ToListAsync();

                if (languages == null || !languages.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No languages found for the provided employee ID.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee language proficiency details retrieved successfully.",
                    Result = languages
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }


        [HttpGet]
        [ActionName("GetEmployeeLanguage")]
        [Authorize]
        public VivifyResponse<object> GetEmployeeLanguage([FromQuery] long? empNo)
        {
            try
            {
                IQueryable<EmployeeLanguage> query = CommonDBContext.EmployeeLanguages;

                if (empNo.HasValue && empNo > 0)
                {
                    query = query.Where(lang => lang.EmpId == empNo.Value);
                }

                var languageRecords = (
                    from l in query
                    join lang in CommonDBContext.Languages on l.LanguageId equals lang.LanguageId
                    join read in CommonDBContext.SkillLevels on l.ReadLevelId equals read.Id
                    join write in CommonDBContext.SkillLevels on l.WriteLevelId equals write.Id
                    join speak in CommonDBContext.SkillLevels on l.SpeakLevelId equals speak.Id
                    select new
                    {
                        l.EmpId,
                        l.LanguageId,
                        LanguageName = lang.LanguageName,
                        l.LastAssessed,
                        l.ReadLevelId,
                        ReadLevel = read.Name,
                        l.WriteLevelId,
                        WriteLevel = write.Name,
                        l.SpeakLevelId,
                        SpeakLevel = speak.Name
                    }
                ).ToList();

                if (!languageRecords.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No language records found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee language records retrieved successfully.",
                    Result = languageRecords
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }



        [HttpPost]
        [ActionName("UploadEmployeeCertification")]
        public async Task<VivifyResponse<object>> UploadEmployeeCertification([FromForm] CertificationRequest request)
        {
            try
            {
                if (request.File == null || request.File.Length == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Certification file is required.",
                        Result = null
                    };
                }

               
                string certificationPath = await SaveAttachmentFileAsync(request.File);

          
                var certification = new EmployeeCertification
                {
                    EmpId = request.EmpId,
                    Cert_Desc = request.Cert_Desc,
                    Certification_Number = request.Certification_Number,
                    IssueDate = request.IssueDate,
                    ExpiryDate = request.ExpiryDate,
                    Certification_Path = certificationPath,
                };

                CommonDBContext.EmployeeCertifications.Add(certification);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Certification uploaded successfully.",
                    Result = new { EmpId = certification.EmpId, CertificationId = certification.CertificationID }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmployeeCertifications")]
        public async Task<VivifyResponse<object>> GetEmployeeCertifications([FromQuery] long? empId)
        {
            try
            {
              
                IQueryable<EmployeeCertification> certificationQuery = CommonDBContext.EmployeeCertifications;

                if (empId.HasValue && empId > 0)
                {
                    certificationQuery = certificationQuery.Where(c => c.EmpId == empId.Value);
                }

                var certifications = await certificationQuery
                    .Select(c => new
                    {
                        c.CertificationID,
                        c.EmpId,
                        c.Cert_Desc,
                        c.Certification_Number,
                        c.IssueDate,
                        c.ExpiryDate,
                        c.Certification_Path
                    })
                    .ToListAsync();

                if (certifications == null || !certifications.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No certifications found for the provided employee ID.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee certifications retrieved successfully.",
                    Result = certifications
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
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
        [ActionName("GetDocumentTypes")]
        public VivifyResponse<List<DocumentTypeResponse>> GetDocumentTypes()
        {
            try
            {
              
                var documentTypes = CommonDBContext.DocumentTypes
                    .Select(x => new DocumentTypeResponse
                    {
                        DocumentTypeID = x.DocumentTypeId,
                        DocumentTypeName = x.DocumentTypeName
                    })
                    .ToList();

                return new VivifyResponse<List<DocumentTypeResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Document types fetched successfully.",
                    Result = documentTypes
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<DocumentTypeResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<DocumentTypeResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetInsuranceTypes")]
        public VivifyResponse<List<InsuranceResponse>> GetInsuranceTypes()
        {
            try
            {
                var insuranceTypes = CommonDBContext.Insurances
                    .Select(x => new InsuranceResponse
                    {
                        InsuranceID = x.InsuranceId,
                        InsuranceName = x.InsuranceName,
                        Status = x.Status
                    })
                    .ToList();

                return new VivifyResponse<List<InsuranceResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Insurance types fetched successfully.",
                    Result = insuranceTypes
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<InsuranceResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<InsuranceResponse>()
                };
            }
        }
    
        [HttpGet]
        [ActionName("GetStandardDocumentTypes")]
        public VivifyResponse<List<DocumentTypeResponse>> GetStandardDocumentTypes()
        {
            try
            {                var standardDocNames = new List<string>
        {
            "PAN Card",
            "Aadhar Card",
            "Signature Copy",
            "Passport",
            "Hand Book"
        };

                var documentTypes = CommonDBContext.DocumentTypes
                    .Where(dt => standardDocNames.Contains(dt.DocumentTypeName))
                    .Select(dt => new DocumentTypeResponse
                    {
                        DocumentTypeID = dt.DocumentTypeId,
                        DocumentTypeName = dt.DocumentTypeName
                    })
                    .OrderBy(dt => dt.DocumentTypeID)
                    .ToList();

                return new VivifyResponse<List<DocumentTypeResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Standard document types fetched successfully.",
                    Result = documentTypes
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<DocumentTypeResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<DocumentTypeResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmployeeOthersInfo")]
        [Authorize]
        public async Task<VivifyResponse<object>> GetEmployeeOthersInfo([FromQuery] long? empNo)
        {
            try
            {
              
                if (!empNo.HasValue)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Employee number (empNo) is required.",
                        Result = null
                    };
                }

               
                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == empNo);

                if (employee == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = null
                    };
                }

                
                var confirmation = await CommonDBContext.Confirmations
                    .FirstOrDefaultAsync(c => c.EmpNo == empNo);

                var aepPass = await CommonDBContext.AEP_PassInfo
                    .FirstOrDefaultAsync(a => a.EmpNo == empNo);

                var medicalTest = await CommonDBContext.MedicalInfos
                    .FirstOrDefaultAsync(m => m.EmpNo == empNo);

                var covidDoc = await CommonDBContext.Document
                    .Where(d => d.DocOwnerId == empNo && d.DocumentTypeId == 13)
                    .OrderByDescending(d => d.DateUpdated)
                    .Select(d => d.DocumentLocation)
                    .FirstOrDefaultAsync();

             
                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee other info retrieved successfully.",
                    Result = new
                    {
                        EmpNo = employee.EmpNo,
                        Height = employee.Height,
                        Weight = employee.Weight,
                        ShoeSize = employee.ShoeSize,
                        TShirtSize = employee.TshirtSize,

                        Confirmation = confirmation == null ? null : new
                        {
                            confirmation.ShoeStatus,
                            confirmation.HelmetStatus,
                            confirmation.TshirtStatus,
                            confirmation.IdCardStatus
                        },

                        AEP_Pass = aepPass == null ? null : new
                        {
                            aepPass.AEPNumber,
                            aepPass.IssuedDate,
                            aepPass.ExpiryDate,
                            aepPass.Documents
                        },

                        MedicalTest = medicalTest == null ? null : new
                        {
                            medicalTest.TestDate,
                            medicalTest.ExpiryDate,
                            medicalTest.Documents
                        },

                        CovidDocPath = covidDoc
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmployeeOthersInfo")]
        public async Task<VivifyResponse<object>> AddEmployeeOthersInfo([FromForm] PhysicalInfoRequest request)
        {
            try
            {
                // Retrieve the employee with tracking so we can modify it
                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == request.EmpNo);

                if (employee == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found."
                    };
                }

                // ✅ Update physical info fields
                employee.ShoeSize = request.ShoeSize;
                employee.Height = request.Height;
                employee.Weight = request.Weight;
                employee.TshirtSize = request.TShirtSize;

                // Format virtual path to full URL
                string FormatDocumentUrl(string virtualPath)
                {
                    if (string.IsNullOrEmpty(virtualPath)) return null;
                    if (virtualPath.StartsWith("http"))
                        return virtualPath;
                    return $"https://www.vivifysoft.in{virtualPath}";
                }

                // Add Confirmation record if not present
                var confirmation = await CommonDBContext.Confirmations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.EmpNo == request.EmpNo);

                if (confirmation == null)
                {
                    confirmation = new Confirmation
                    {
                        EmpNo = request.EmpNo,
                        ShoeStatus = request.ShoeStatus ?? false,
                        HelmetStatus = request.HelmetStatus ?? false,
                        TshirtStatus = request.TShirtStatus ?? false,
                        IdCardStatus = request.IdCardStatus ?? false,
                        UpdatedDate = DateTime.Now
                    };
                    CommonDBContext.Confirmations.Add(confirmation);
                }

                // Add AEP pass info if applicable
                var aepPass = await CommonDBContext.AEP_PassInfo
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.EmpNo == request.EmpNo);

                if (aepPass == null && (request.IssuedDate != null || request.ExpiryDate != null || request.AEPDocument != null))
                {
                    aepPass = new AEP_PassInfo
                    {
                        EmpNo = request.EmpNo,
                        IssuedDate = request.IssuedDate,
                        ExpiryDate = request.ExpiryDate,
                        AEPNumber = request.AEPNumber,
                        UpdatedDate = DateTime.Now
                    };

                    if (request.AEPDocument != null && request.AEPDocument.Length > 0)
                    {
                        var savedVirtualPath = await SaveAttachmentFileAsync(request.AEPDocument);
                        aepPass.Documents = FormatDocumentUrl(savedVirtualPath);
                    }

                    CommonDBContext.AEP_PassInfo.Add(aepPass);
                }

                // Add medical info if not already present
                var medicalTest = await CommonDBContext.MedicalInfos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.EmpNo == request.EmpNo);

                if (medicalTest == null && (request.TestDate != null || request.MedicalDocument != null))
                {
                    medicalTest = new MedicalInfo
                    {
                        EmpNo = request.EmpNo,
                        TestDate = request.TestDate ?? DateTime.Now,
                        ExpiryDate = (request.TestDate ?? DateTime.Now).AddYears(1),
                        UpdatedDate = DateTime.Now
                    };

                    if (request.MedicalDocument != null && request.MedicalDocument.Length > 0)
                    {
                        var savedVirtualPath = await SaveAttachmentFileAsync(request.MedicalDocument);
                        medicalTest.Documents = FormatDocumentUrl(savedVirtualPath);
                    }

                    CommonDBContext.MedicalInfos.Add(medicalTest);
                }

                // Save COVID document if uploaded
                if (request.CovidDoc != null && request.CovidDoc.Length > 0)
                {
                    var savedVirtualPath = await SaveAttachmentFileAsync(request.CovidDoc);

                    var docType = await CommonDBContext.DocumentTypes.FindAsync(13);
                    if (docType == null)
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 400,
                            StatusDesc = "Document type with ID 13 not found.",
                            Result = null
                        };
                    }

                    var document = new Documents
                    {
                        DocOwnerId = request.EmpNo,
                        DocumentTypeId = 13,
                        DocumentName = docType.DocumentTypeName,
                        DateUpdated = DateTime.Now,
                        DocumentLocation = FormatDocumentUrl(savedVirtualPath)
                    };

                    CommonDBContext.Document.Add(document);
                }

                // ✅ Save all changes including updates to EmployeeInfo
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee details added successfully.",
                    Result = new { EmpNo = request.EmpNo }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }



        [HttpPost]
        [ActionName("UpdateEmployeeOthersInfo")]
        public async Task<VivifyResponse<object>> UpdateEmployeeOthersInfo([FromForm] PhysicalInfoRequest request)
        {
            try
            {
                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == request.EmpNo);

                if (employee == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found."
                    };
                }

                // Basic Info
                employee.ShoeSize = request.ShoeSize;
                employee.TshirtSize = request.TShirtSize;
                employee.Height = request.Height;
                employee.Weight = request.Weight;
                CommonDBContext.EmployeeInfos.Update(employee);

                // Confirmation Info
                var confirmation = await CommonDBContext.Confirmations
                    .FirstOrDefaultAsync(c => c.EmpNo == request.EmpNo);

                if (confirmation != null)
                {
                    confirmation.ShoeStatus = request.ShoeStatus ?? confirmation.ShoeStatus;
                    confirmation.HelmetStatus = request.HelmetStatus ?? confirmation.HelmetStatus;
                    confirmation.TshirtStatus = request.TShirtStatus ?? confirmation.TshirtStatus;
                    confirmation.IdCardStatus = request.IdCardStatus ?? confirmation.IdCardStatus;
                    confirmation.UpdatedDate = DateTime.Now;
                    CommonDBContext.Confirmations.Update(confirmation);
                }

                string FormatDocumentUrl(string fileName)
                {
                    if (string.IsNullOrEmpty(fileName)) return null;
                    if (fileName.StartsWith("http")) return fileName;
                    var fileExtension = Path.GetExtension(fileName);
                    return $"https://www.vivifysoft.in/api/Video/{Guid.NewGuid()}{fileExtension}";
                }

                // AEP Pass Info
                var aepPass = await CommonDBContext.AEP_PassInfo
                    .FirstOrDefaultAsync(a => a.EmpNo == request.EmpNo);

                if (aepPass != null)
                {
                    if (!string.IsNullOrEmpty(request.AEPNumber)) aepPass.AEPNumber = request.AEPNumber;
                    if (request.IssuedDate != null) aepPass.IssuedDate = request.IssuedDate;
                    if (request.ExpiryDate != null) aepPass.ExpiryDate = request.ExpiryDate;

                    // Only update document if a new one is provided
                    if (!string.IsNullOrEmpty(request.AEPDocumentUrl))
                    {
                        aepPass.Documents = request.AEPDocumentUrl.Contains("404") ? null : request.AEPDocumentUrl;
                    }
                    else if (request.AEPDocument == null || request.AEPDocument.Length == 0)
                    {
                        // Keep existing or set to null if none provided
                        aepPass.Documents = " ";
                    }

                    aepPass.UpdatedDate = DateTime.Now;
                    CommonDBContext.AEP_PassInfo.Update(aepPass);
                }

                // Medical Test Info
                var medicalTest = await CommonDBContext.MedicalInfos
                    .FirstOrDefaultAsync(m => m.EmpNo == request.EmpNo);

                if (medicalTest != null)
                {
                    if (request.TestDate != null)
                    {
                        medicalTest.TestDate = request.TestDate.Value;
                        medicalTest.ExpiryDate = request.TestDate.Value.AddYears(1);
                    }

                    if (request.MedicalDocument != null && request.MedicalDocument.Length > 0)
                    {
                        var savedPath = await SaveAttachmentFileAsync(request.MedicalDocument);
                        medicalTest.Documents = FormatDocumentUrl(savedPath);
                    }
                    else
                    {
                        // Set to null if no file is uploaded
                        medicalTest.Documents = null;
                    }

                    medicalTest.UpdatedDate = DateTime.Now;
                    CommonDBContext.MedicalInfos.Update(medicalTest);
                }

                // Vaccination Certificate
                var covidDoc = await CommonDBContext.Document
                    .FirstOrDefaultAsync(d => d.DocOwnerId == request.EmpNo && d.DocumentTypeId == 13);

                if (covidDoc != null)
                {
                    if (!string.IsNullOrEmpty(request.CovidDocUrl) && !request.CovidDocUrl.Contains("404"))
                    {
                        covidDoc.DocumentLocation = FormatDocumentUrl(request.CovidDocUrl);
                    }
                    else
                    {
                        // No new file → keep or set to null
                        covidDoc.DocumentLocation = null;
                    }

                    covidDoc.DateUpdated = DateTime.Now;
                    CommonDBContext.Document.Update(covidDoc);
                }

                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee details updated successfully.",
                    Result = new { EmpNo = employee.EmpNo }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }




        [HttpPost]
        [ActionName("UploadAndAddDocument")]
        public async Task<VivifyResponse<object>> UploadAndAddDocument(
      [FromForm] IFormFile file,
      [FromForm] int DocumentTypeID,
      [FromForm] int Doc_OwnerID)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "No file uploaded.",
                        Result = null
                    };
                }

              
                var documentType = await CommonDBContext.DocumentTypes.FindAsync(DocumentTypeID);
                if (documentType == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid DocumentTypeID.",
                        Result = null
                    };
                }

            
                var existingDocument = CommonDBContext.Document
                    .FirstOrDefault(d => d.DocOwnerId == Doc_OwnerID && d.DocumentTypeId== DocumentTypeID);

                if (existingDocument != null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = $" '{existingDocument.DocumentName}' already uploaded.",
                        Result = new
                        {
                            DocumentID = existingDocument.DocumentId,
                            DocumentName = existingDocument.DocumentName,
                            DocumentLocation = existingDocument.DocumentLocation
                        }
                    };
                }

            
                var filePath = await SaveAttachmentFileAsync(file);
                if (string.IsNullOrEmpty(filePath))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "File location is invalid.",
                        Result = null
                    };
                }

                var document = new Documents
                {
                    DocumentLocation = filePath,
                    DocumentTypeId = DocumentTypeID,
                    DocOwnerId = Doc_OwnerID,
                    DateUpdated = DateTime.Now,
                    DocumentName = $"{documentType.DocumentTypeName}"
                };

                CommonDBContext.Document.Add(document);
                await CommonDBContext.SaveChangesAsync();

        
                if (DocumentTypeID == 12)
                {
                    var companyIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID");
                    if (companyIdClaim == null || !int.TryParse(companyIdClaim.Value, out int companyId))
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 401,
                            StatusDesc = "Invalid CompanyID in token.",
                            Result = null
                        };
                    }

                    var employeePolicy = new EmployeePolicy
                    {
                        DocumentTypeId = document.DocumentTypeId,
                        EmpId = Doc_OwnerID,
                        CompanyId = companyId,
                        UpdDate = DateTime.Now
                    };

                    CommonDBContext.EmployeePolicies.Add(employeePolicy);
                    await CommonDBContext.SaveChangesAsync();
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Document uploaded successfully.",
                    Result = new
                    {
                        DocumentID = document.DocumentId,
                        FilePath = filePath,
                        DocumentName = document.DocumentName,
                        IsEmployeePolicy = (DocumentTypeID == 12) 
                    }
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }


        [HttpGet]
        [ActionName("GetUploadedDocuments")]
        public async Task<VivifyResponse<object>> GetUploadedDocuments([FromQuery] long? empNo)
        {
            try
            {
              
                var documentsQuery = CommonDBContext.Document
                    .Where(d => d.DocumentTypeId != 13) 
                    .Select(d => new
                    {
                        d.DocumentId,
                        d.DocumentName,
                        d.DocumentLocation,
                        d.DateUpdated,
                        d.DocumentTypeId,
                        d.DocOwnerId
                    });

                List<object> documents;

                if (empNo.HasValue)
                {
                  
                    documents = (await documentsQuery
                        .Where(d => d.DocOwnerId == empNo.Value)
                        .ToListAsync())
                        .Cast<object>()
                        .ToList();
                }
                else
                {
                  
                    documents = (await documentsQuery
                        .ToListAsync())
                        .Cast<object>()
                        .ToList();
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Documents retrieved successfully.",
                    Result = documents
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetInsuranceStatus")]
        [Authorize]
        public VivifyResponse<object> GetInsuranceStatus([FromQuery] long? EmpNo = null)
        {
            try
            {
                long? empNo = EmpNo;

                if (empNo == null)
                {
                    
                    var empNoClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo");
                    if (empNoClaim != null && long.TryParse(empNoClaim.Value, out long tokenEmpNo))
                    {
                        empNo = tokenEmpNo;
                    }
                }

                var insuranceList = (from ei in CommonDBContext.EmployeeInsurances
                                     join im in CommonDBContext.Insurances
                                     on ei.Id equals im.InsuranceId
                                     where empNo == null || ei.EmpNo == empNo
                                     select new
                                     {
                                         InsuranceID = ei.Id,
                                         InsuranceName = im.InsuranceName,
                                         Status = ei.Status,
                                         UpdatedDate = ei.UpdatedDate
                                     }).ToList();

                if (!insuranceList.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No insurance records found."
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Insurance records retrieved successfully.",
                    Result = insuranceList
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddEmployeeInsurance")]
        public async Task<VivifyResponse<object>> AddEmployeeInsurance([FromBody] EmployeeInsuranceRequest request)
        {
            try
            {
                if (request.EmpNo <= 0 || request.InsuranceList == null || !request.InsuranceList.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid request. EmpNo and InsuranceList are required.",
                        Result = null
                    };
                }

                foreach (var insuranceItem in request.InsuranceList)
                {
                    var insuranceMaster = await CommonDBContext.Insurances
                        .Where(i => i.InsuranceId == insuranceItem.InsuranceID)
                        .FirstOrDefaultAsync();

                    if (insuranceMaster == null)
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 400,
                            StatusDesc = $"Insurance ID {insuranceItem.InsuranceID} not found.",
                            Result = null
                        };
                    }

                    var existingInsurance = await CommonDBContext.EmployeeInsurances
                        .Where(e => e.EmpNo == request.EmpNo && e.InsuranceName == insuranceMaster.InsuranceName)
                        .FirstOrDefaultAsync();

                    if (existingInsurance != null)
                    {
                        existingInsurance.Status = insuranceItem.Status;
                        existingInsurance.UpdatedDate = DateTime.Now;
                        CommonDBContext.EmployeeInsurances.Update(existingInsurance);
                    }
                    else
                    {
                        var newEmpInsurance = new EmployeeInsurance
                        {
                            EmpNo = request.EmpNo,
                            InsuranceName = insuranceMaster.InsuranceName,
                            Status = insuranceItem.Status,
                            UpdatedDate = DateTime.Now
                        };

                        await CommonDBContext.EmployeeInsurances.AddAsync(newEmpInsurance);
                    }
                }

                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee insurance details updated successfully.",
                    Result = new { EmpNo = request.EmpNo }
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeInsurance")]
        public async Task<VivifyResponse<object>> GetEmployeeInsurance(long? empNo)
        {
            try
            {
                if (empNo.HasValue && empNo.Value <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid EmpNo provided.",
                        Result = null
                    };
                }

                var employeeInsurances = empNo.HasValue
                    ? await CommonDBContext.EmployeeInsurances
                        .Where(e => e.EmpNo == empNo.Value)
                        .Select(e => new
                        {
                            e.EmpNo,
                            e.InsuranceName,
                            e.Status,
                            e.UpdatedDate
                        })
                        .ToListAsync()
                    : await CommonDBContext.EmployeeInsurances
                        .Select(e => new
                        {
                            e.EmpNo,
                            e.InsuranceName,
                            e.Status,
                            e.UpdatedDate
                        })
                        .ToListAsync();

                if (employeeInsurances == null || !employeeInsurances.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No insurance details found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee insurance details fetched successfully.",
                    Result = employeeInsurances
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }
        private bool ValidateFormulaFormat(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula))
                return false;

            var parts = formula.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
                return false;

            bool expectingOperand = true;
            int percentIndex = -1;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];

                if (expectingOperand)
                {
                    if (!int.TryParse(part, out _) && !part.StartsWith("PayComponent_"))
                        return false;

                    expectingOperand = false;
                }
                else
                {
                    if (new[] { "+", "-", "*", "/" }.Contains(part))
                    {
                        expectingOperand = true;
                    }
                    else if (part == "%")
                    {
                        percentIndex = i;
                        break;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            if (percentIndex == -1)
                return !expectingOperand;

            if (percentIndex + 1 >= parts.Length)
                return false;

            string percentValue = parts[percentIndex + 1];

            return decimal.TryParse(percentValue, out decimal percentage) &&
                   percentage > 0 && percentage <= 100;
        }
        [HttpPost]
        [ActionName("AddBasicAllowanceComponent")]
        public VivifyResponse<object> AddBasicAllowanceComponent([FromForm] PayComponentRequest request)
        {
            try
            {
                // Extract EmpNo and CompanyID from JWT token
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value);

                // Optional: Extract BranchId
                int? branchId = null;
                var branchClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "BranchID")?.Value;
                if (!string.IsNullOrEmpty(branchClaim) && int.TryParse(branchClaim, out int brId))
                {
                    branchId = brId;
                }

                // Validate required input
                if (string.IsNullOrWhiteSpace(request.PayComponentName))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Pay component name is required.",
                        Result = null
                    };
                }

                PayComponent payComponent = null;

                // Try to find by ID first
                if (request.PayComponentID > 0)
                {
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentId == request.PayComponentID && pc.PayComponentTypeId == 1);
                }

                // If not found by ID, try to find by name
                if (payComponent == null)
                {
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentName == request.PayComponentName && pc.PayComponentTypeId == 1);
                }

                bool isUpdating = payComponent != null;

                // Only validate formula if IsFormula is true
                if (request.IsFormula)
                {
                    if (string.IsNullOrWhiteSpace(request.Formula))
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 400,
                            StatusDesc = "Formula is required when 'IsFormula' is true.",
                            Result = null
                        };
                    }

                    if (!ValidateFormulaFormat(request.Formula))
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 400,
                            StatusDesc = "Invalid formula format. Expected format: 'PayComponent_X % Y' or '123 % 50'",
                            Result = null
                        };
                    }
                }

                if (isUpdating)
                {
                    // Update existing component
                    payComponent.PayComponentName = request.PayComponentName;
                    payComponent.ShortName = request.ShortName;
                    payComponent.PayFrequencyId = request.PayFrequencyID;
                    payComponent.Status = request.Status;
                    payComponent.LastEditedBy = (int)empNo;
                    payComponent.LastEditedDate = DateTime.Now;
                    payComponent.CompanyId = companyId;
                    payComponent.BranchId = branchId;

                    payComponent.IsFormula = request.IsFormula;

                    if (request.IsFormula)
                    {
                        payComponent.Formula = request.Formula.Trim();
                    }
                    else
                    {
                        payComponent.Formula = null; // Clear formula if not needed
                    }

                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = request.Status
                            ? "Basic and Allowance component updated successfully."
                            : "Basic and Allowance component deleted successfully.",
                        Result = new { payComponent.PayComponentId }
                    };
                }
                else
                {
                    // Create new component
                    payComponent = new PayComponent
                    {
                        PayComponentName = request.PayComponentName,
                        ShortName = request.ShortName,
                        PayComponentTypeId = 1,
                        PayFrequencyId = request.PayFrequencyID,
                        Status = request.Status,
                        LastEditedBy = (int)empNo,
                        LastEditedDate = DateTime.Now,
                        CompanyId = companyId,
                        BranchId = branchId,
                        IsFormula = request.IsFormula,
                        Formula = request.IsFormula ? request.Formula?.Trim() : null
                    };

                    CommonDBContext.PayComponents.Add(payComponent);
                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Basic and Allowance component added successfully.",
                        Result = new { payComponent.PayComponentId }
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetBasicAllowanceComponents")]
        public VivifyResponse<object> GetBasicAllowanceComponents()
        {
            try
            {
                var activeComponents = CommonDBContext.PayComponents
                    .Where(pc => pc.PayComponentTypeId == 1 && pc.Status == true)
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayFrequencyId,
                        pc.AccountCode,
                        pc.AccountDescription,
                        pc.Status,
                        pc.IsFormula,
                        pc.Formula,
                        pc.LastEditedBy,
                        pc.LastEditedDate
                     
                    })
                    .ToList();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Active basic and allowance components fetched successfully.",
                    Result = activeComponents
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }


        [HttpGet]
        [ActionName("EditBasicAllowanceComponent")]
        public VivifyResponse<object> EditBasicAllowanceComponent(long payComponentId)
        {
            try
            {
                var component = CommonDBContext.PayComponents
                    .Where(pc => pc.PayComponentId == payComponentId && pc.PayComponentTypeId == 1)
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayFrequencyId,
                        pc.Status,
                        pc.IsFormula,
                        pc.Formula,
                        pc.PayComponentTypeId
                    })
                    .FirstOrDefault();

                if (component == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Basic or Allowance component not found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Basic or Allowance component fetched successfully.",
                    Result = component
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddIncentiveComponent")]
        public VivifyResponse<object> AddIncentiveComponent([FromForm] PayComponentRequest request)
        {
            try
            {
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);

                PayComponent payComponent = null;
                bool isUpdate = false;

                if (request.PayComponentID > 0)
                {
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentId == request.PayComponentID && pc.PayComponentTypeId == 2);

                    if (payComponent == null)
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 404,
                            StatusDesc = "Incentive component not found.",
                            Result = null
                        };
                    }

                    isUpdate = true;
                }
                else
                {
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentName == request.PayComponentName && pc.PayComponentTypeId == 2);

                    if (payComponent != null)
                        isUpdate = true;
                }

                if (payComponent != null)
                {
                    payComponent.ShortName = request.ShortName;
                    payComponent.PayFrequencyId = request.PayFrequencyID;
                    payComponent.Status = request.Status;
                    payComponent.LastEditedBy = (int)empNo;
                    payComponent.LastEditedDate = DateTime.Now;
                }
                else
                {
                    payComponent = new PayComponent
                    {
                        PayComponentName = request.PayComponentName,
                        ShortName = request.ShortName,
                        PayComponentTypeId = 2,
                        PayFrequencyId = request.PayFrequencyID,
                        Status = request.Status,
                        LastEditedBy = (int)empNo,
                        LastEditedDate = DateTime.Now
                    };

                    CommonDBContext.PayComponents.Add(payComponent);
                }

                CommonDBContext.SaveChanges();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = !request.Status ? "Incentive component deleted successfully."
                                : isUpdate ? "Incentive component updated successfully."
                                : "Incentive component added successfully.",
                    Result = new { payComponent.PayComponentId }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("EditIncentiveComponent")]
        public VivifyResponse<object> EditIncentiveComponent(long payComponentId)
        {
            try
            {
                var component = CommonDBContext.PayComponents
                    .Where(pc => pc.PayComponentId == payComponentId && pc.PayComponentTypeId == 2)
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayFrequencyId,
                        pc.Status,
                        pc.PayComponentTypeId
                    })
                    .FirstOrDefault();

                if (component == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Incentive component not found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Incentive component fetched successfully.",
                    Result = component
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetIncentiveComponents")]
        public VivifyResponse<object> GetIncentiveComponents()
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value);
                int? branchId = null;
                var branchClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "BranchCode")?.Value;

               

                IQueryable<PayComponent> query = CommonDBContext.PayComponents
                    .Where(pc => pc.PayComponentTypeId == 2 &&
                                 pc.Status == true 
                               );

               

                var activeIncentives = query
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayComponentTypeId,
                        pc.PayFrequencyId,
                        pc.AccountCode,
                        pc.AccountDescription,
                        pc.Status,
                        pc.LastEditedBy,
                        pc.LastEditedDate,
                     
                    })
                    .ToList();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Active incentive components fetched successfully.",
                    Result = activeIncentives
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddDeductionComponent")]
        public VivifyResponse<object> AddDeductionComponent([FromForm] PayComponentRequest request)
        {
            try
            {
                // Extract EmpNo and CompanyID from JWT token
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value);

                // Optional: Extract BranchId
                int? branchId = null;
                var branchClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "BranchID")?.Value;
                if (!string.IsNullOrEmpty(branchClaim) && int.TryParse(branchClaim, out int brId))
                {
                    branchId = brId;
                }

                // Validate required input
                if (string.IsNullOrWhiteSpace(request.PayComponentName))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Pay component name is required.",
                        Result = null
                    };
                }

                PayComponent payComponent = null;
                bool isUpdating = false;

                // Try to find by ID first
                if (request.PayComponentID > 0)
                {
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentId == request.PayComponentID && pc.PayComponentTypeId == 2);
                }

                // If not found by ID, try to find by name
                if (payComponent == null)
                {
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentName == request.PayComponentName &&
                                              pc.PayFrequencyId == request.PayFrequencyID &&
                                              pc.PayComponentTypeId == 3);
                }

                isUpdating = payComponent != null;

                // Only validate formula if IsFormula is true
                if (request.IsFormula)
                {
                    if (string.IsNullOrWhiteSpace(request.Formula))
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 400,
                            StatusDesc = "Formula is required when 'IsFormula' is true.",
                            Result = null
                        };
                    }

                    if (!ValidateFormulaFormat(request.Formula))
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 400,
                            StatusDesc = "Invalid formula format. Expected format: 'PayComponent_X % Y' or '123 % 50'",
                            Result = null
                        };
                    }
                }

                if (isUpdating)
                {
                    // Update existing deduction component
                    payComponent.PayComponentName = request.PayComponentName;
                    payComponent.ShortName = request.ShortName;
                    payComponent.PayFrequencyId = request.PayFrequencyID;
                    payComponent.Status = request.Status;
                    payComponent.LastEditedBy = (int)empNo;
                    payComponent.LastEditedDate = DateTime.Now;
                    payComponent.CompanyId = companyId;
                    payComponent.BranchId = branchId;
                    payComponent.IsFormula = request.IsFormula;

                    if (request.IsFormula)
                    {
                        payComponent.Formula = request.Formula.Trim();
                    }
                    else
                    {
                        payComponent.Formula = null; // Clear formula if not needed
                    }

                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = request.Status
                            ? "Deduction component updated successfully."
                            : "Deduction component deleted successfully.",
                        Result = new { payComponent.PayComponentId }
                    };
                }
                else
                {
                    // Create new deduction component
                    payComponent = new PayComponent
                    {
                        PayComponentName = request.PayComponentName,
                        ShortName = request.ShortName,
                        PayComponentTypeId = 2, // Deduction type
                        PayFrequencyId = request.PayFrequencyID,
                        Status = request.Status,
                        LastEditedBy = (int)empNo,
                        LastEditedDate = DateTime.Now,
                        CompanyId = companyId,
                        BranchId = branchId,
                        IsFormula = request.IsFormula,
                        Formula = request.IsFormula ? request.Formula?.Trim() : null
                    };

                    CommonDBContext.PayComponents.Add(payComponent);
                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Deduction component added successfully.",
                        Result = new { payComponent.PayComponentId }
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddNonPayrollComponent")]
        public VivifyResponse<object> AddNonPayrollComponent([FromForm] PayComponentRequest request)
        {
            try
            {
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);

                PayComponent payComponent = null;
                bool isUpdate = false;

                if (request.PayComponentID > 0)
                {
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentId == request.PayComponentID && pc.PayComponentTypeId == 4); 

                    if (payComponent == null)
                    {
                        return new VivifyResponse<object>
                        {
                            StatusCode = 404,
                            StatusDesc = "Benefit component not found.",
                            Result = null
                        };
                    }

                    isUpdate = true;
                }
                else
                {
                   
                    payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentName == request.PayComponentName &&
                                              pc.PayFrequencyId == request.PayFrequencyID &&
                                              pc.PayComponentTypeId == 4);

                    if (payComponent != null)
                    {
                        isUpdate = true;
                    }
                }

                if (payComponent != null)
                {
                    if (isUpdate)
                    {
                        payComponent.ShortName = request.ShortName;
                        payComponent.PayFrequencyId = request.PayFrequencyID;
                        payComponent.Status = request.Status;
                        payComponent.LastEditedBy = (int)empNo;
                        payComponent.LastEditedDate = DateTime.Now;
                    }
                }
                else
                {
                    payComponent = new PayComponent
                    {
                        PayComponentName = request.PayComponentName,
                        ShortName = request.ShortName,
                        PayComponentTypeId = 4, 
                        PayFrequencyId = request.PayFrequencyID,
                        Status = request.Status,
                        LastEditedBy = (int)empNo,
                        LastEditedDate = DateTime.Now
                    };

                    CommonDBContext.PayComponents.Add(payComponent);
                }

                CommonDBContext.SaveChanges();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = !request.Status ? "Benefit component deleted successfully."
                                : isUpdate ? "Benefit component updated successfully."
                                : "Benefit component added successfully.",
                    Result = new { payComponent.PayComponentId }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetNonPayrollComponents")]
        public VivifyResponse<object> GetNonPayrollComponents()
        {
            try
            {
              
                var nonPayrollTypes = new List<int> { 4, 5 };

                var components = CommonDBContext.PayComponents
                    .Where(pc => nonPayrollTypes.Contains(pc.PayComponentTypeId) && pc.Status == true)
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayFrequencyId,
                        pc.AccountCode,
                        pc.AccountDescription,
                        pc.PayComponentTypeId,
                        pc.Status,
                        pc.LastEditedBy,
                        pc.LastEditedDate
                    })
                    .ToList();

                if (components.Count == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active non-payroll components found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Active non-payroll components fetched successfully.",
                    Result = components
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("EditNonPayrollComponent")]
        public VivifyResponse<object> EditNonPayrollComponent(long payComponentId)
        {
            try
            {
                var component = CommonDBContext.PayComponents
                    .Where(pc => pc.PayComponentId == payComponentId && pc.PayComponentTypeId == 4)
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayFrequencyId,
                        pc.Status,
                        pc.PayComponentTypeId
                    })
                    .FirstOrDefault();

                if (component == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Benefit component not found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Benefit component fetched successfully.",
                    Result = component
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmployeeNonPayroll")]
        public async Task<VivifyResponse<object>> AddEmployeeNonPayroll([FromForm] EmployeeNonPayrollRequest request)
        {
            try
            {
            
                if (request.EmpID <= 0 || request.ComponentPayID <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid request. EmpID and ComponentPayID are required.",
                        Result = null
                    };
                }

                long updatedByEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value);

              
                int finalIsActive = request.IsActive.HasValue && request.IsActive.Value ? 1 : 0; 
             
                var existingComponent = await CommonDBContext.EmployeeNonPayrolls
                    .FirstOrDefaultAsync(x => x.EmpId == request.EmpID && x.ComponentPayId == request.ComponentPayID);

                if (existingComponent != null)
                {
                   
                    if (finalIsActive == 0)
                    {
                        existingComponent.IsActive = false;
                        existingComponent.UpdDate = DateTime.Now;
                        existingComponent.UpdBy = (int)updatedByEmpNo;

                        CommonDBContext.EmployeeNonPayrolls.Update(existingComponent);
                        await CommonDBContext.SaveChangesAsync();

                        return new VivifyResponse<object>
                        {
                            StatusCode = 200,
                            StatusDesc = "NonPayroll component delete.",
                            Result = new { EmpID = request.EmpID }
                        };
                    }
                    else
                    {
                       
                        existingComponent.IsActive = true;
                        existingComponent.UpdDate = DateTime.Now;
                        existingComponent.UpdBy = (int)updatedByEmpNo;

                        CommonDBContext.EmployeeNonPayrolls.Update(existingComponent);
                        await CommonDBContext.SaveChangesAsync();

                        return new VivifyResponse<object>
                        {
                            StatusCode = 200,
                            StatusDesc = "NonPayroll component Updated successfully.",
                            Result = new { EmpID = request.EmpID }
                        };
                    }
                }

               
                var newComponent = new EmployeeNonPayroll
                {
                    EmpId = request.EmpID,
                    ComponentPayId = request.ComponentPayID,
                    IsActive = finalIsActive == 1, 
                    Status = true, 
                    UpdDate = DateTime.Now,
                    UpdBy = (int)updatedByEmpNo
                };

                await CommonDBContext.EmployeeNonPayrolls.AddAsync(newComponent);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee non-payroll component added successfully.",
                    Result = new { EmpID = request.EmpID }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeNonPayroll")]
        public async Task<VivifyResponse<object>> GetEmployeeNonPayroll(int empId)
        {
            try
            {
                if (empId <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid request. EmpID is required.",
                        Result = null
                    };
                }

                // Join EmployeeNonPayroll with PayComponent on ComponentPayId
                var components = await (from np in CommonDBContext.EmployeeNonPayrolls
                                        join pc in CommonDBContext.PayComponents
                                          on np.ComponentPayId equals pc.PayComponentId
                                        where np.EmpId == empId
                                        select new
                                        {
                                            np.ComponentPayId,
                                            np.Status,
                                            np.UpdBy,
                                            np.UpdDate,
                                            pc.PayComponentName,
                                            np.IsActive
                                        }).ToListAsync();

                var activeComponents = components.Where(c => c.IsActive.GetValueOrDefault()).ToList();

                if (!activeComponents.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active non-payroll components found for the specified employee.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee active non-payroll components retrieved successfully.",
                    Result = new
                    {
                        EmpID = empId,
                        ComponentList = activeComponents.Select(c => new
                        {
                            c.ComponentPayId,
                            c.PayComponentName,
                            c.Status,
                            c.UpdBy,
                            c.UpdDate
                        })
                    }
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }


        [HttpGet]
        [ActionName("GetDeductionComponents")]
        public VivifyResponse<object> GetDeductionComponents()
        {
            try
            {
                var activeDeductions = CommonDBContext.PayComponents
                    .Where(pc => pc.PayComponentTypeId == 2 && pc.Status == true)
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayFrequencyId,
                        pc.AccountCode,
                        pc.AccountDescription,
                        pc.Status,
                        pc.IsFormula,
                        pc.Formula,
                        pc.LastEditedBy,
                        pc.LastEditedDate
    
                    })
                    .ToList();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Active deduction components fetched successfully.",
                    Result = activeDeductions
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("EditDeductionComponent")]
        public VivifyResponse<object> EditDeductionComponent(long payComponentId)
        {
            try
            {
                var component = CommonDBContext.PayComponents
                    .Where(pc => pc.PayComponentId == payComponentId && pc.PayComponentTypeId == 2)
                    .Select(pc => new
                    {
                        pc.PayComponentId,
                        pc.PayComponentName,
                        pc.ShortName,
                        pc.PayFrequencyId,
                        pc.Status,
                        pc.IsFormula,
                        pc.Formula,
                        pc.PayComponentTypeId
                    })
                    .FirstOrDefault();

                if (component == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Deduction component not found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Deduction component fetched successfully.",
                    Result = component
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetBenefitTypes")]
        public VivifyResponse<List<PayComponentNameResponse>> GetBenefitTypes()
        {
            try
            {
                var payComponentNames = CommonDBContext.PayComponents
                    .Where(x => x.PayComponentTypeId == 1 && x.Status == true)
                    .Select(x => new PayComponentNameResponse
                    {
                        PayComponentID = x.PayComponentId,
                        PayComponentName = x.PayComponentName,
                        PayComponentTypeID=x.PayComponentTypeId
                    })
                    .ToList();

                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Pay component names fetched successfully.",
                    Result = payComponentNames
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<PayComponentNameResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetIncentiveTypes")]
        public VivifyResponse<List<PayComponentNameResponse>> GetIncentiveTypes()
        {
            try
            {
                var incentives = CommonDBContext.PayComponents
                    .Where(x => x.PayComponentTypeId == 2 && x.Status == true)
                    .Select(x => new PayComponentNameResponse
                    {
                        PayComponentID= x.PayComponentId,
                        PayComponentName = x.PayComponentName,
                        PayComponentTypeID=x.PayComponentTypeId
                    })
                    .ToList();

                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Incentive pay components fetched successfully.",
                    Result = incentives
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<PayComponentNameResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetDeductionTypes")]
        public VivifyResponse<List<PayComponentNameResponse>> GetDeductionTypes()
        {
            try
            {
                var deductions = CommonDBContext.PayComponents
                    .Where(x => x.PayComponentTypeId == 2 && x.Status == true)
                    .Select(x => new PayComponentNameResponse
                    {
                        PayComponentID = x.PayComponentId,
                        PayComponentName = x.PayComponentName,
                        PayComponentTypeID=x.PayComponentTypeId
                    })
                    .ToList();

                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Deduction pay components fetched successfully.",
                    Result = deductions
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<PayComponentNameResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetNonPayrollTypes")]
        public VivifyResponse<List<PayComponentNameResponse>> GetNonPayrollTypes()
        {
            try
            {
                var nonPayrolls = CommonDBContext.PayComponents
                    .Where(x => x.PayComponentTypeId == 4 && x.Status == true)
                    .Select(x => new PayComponentNameResponse
                    {
                        PayComponentID = x.PayComponentId,
                        PayComponentName = x.PayComponentName,
                        PayComponentTypeID = x.PayComponentTypeId
                    })
                    .ToList();

                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Non-payroll components fetched successfully.",
                    Result = nonPayrolls
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<PayComponentNameResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<PayComponentNameResponse>()
                };
            }
        }


        [HttpPost]
        [ActionName("AddEmployeeBenefit")]
        public VivifyResponse<string> AddEmployeeBenefit([FromForm] EmployeeBenefitRequest request)
        {
            try
            {
                
                long editedByEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value);

             
                var existingBenefit = CommonDBContext.EmployeeBenefits
                    .FirstOrDefault(eb => eb.EmpId == request.EmpID && eb.PayComponentId == request.PayComponentID);

                if (existingBenefit != null)
                {
                   
                    if (existingBenefit.Amount != request.Amount ||
                        existingBenefit.ChangeReason != request.ChangeReason ||
                        existingBenefit.ChangeEffectiveDate != (request.ChangeReason == "Revision" ? request.ChangeEffectiveDate : null))
                    {
                      
                        existingBenefit.Amount = request.Amount;
                        existingBenefit.ChangeReason = request.ChangeReason;
                        existingBenefit.ChangeEffectiveDate = request.ChangeReason == "Revision" ? request.ChangeEffectiveDate : null;
                        existingBenefit.LastUpdatedByEmpId = (int)editedByEmpNo;
                        existingBenefit.LastUpdatedDate = DateTime.Now;

                        CommonDBContext.EmployeeBenefits.Update(existingBenefit);
                        CommonDBContext.SaveChanges();

                        return new VivifyResponse<string>
                        {
                            StatusCode = 200,
                            StatusDesc = "Employee benefit updated successfully.",
                            Result = existingBenefit.PayComponentId.ToString() 
                        };
                    }
                    else
                    {
                  
                        return new VivifyResponse<string>
                        {
                            StatusCode = 200,
                            StatusDesc = "Employee benefit already up-to-date.",
                            Result = existingBenefit.PayComponentId.ToString()  
                        };
                    }
                }
                else
                {
                    
                    var newBenefit = new EmployeeBenefit
                    {
                        EmpId = request.EmpID,
                        PayComponentId = request.PayComponentID,
                        Amount = request.Amount,
                        ChangeReason = request.ChangeReason,
                        ChangeEffectiveDate = request.ChangeReason == "Revision" ? request.ChangeEffectiveDate : null,
                        IsActive = true,
                        LastUpdatedByEmpId = (int)editedByEmpNo,
                        LastUpdatedDate = DateTime.Now
                    };

                    CommonDBContext.EmployeeBenefits.Add(newBenefit);
                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<string>
                    {
                        StatusCode = 200,
                        StatusDesc = "Employee benefit added successfully.",
                        Result = newBenefit.PayComponentId.ToString()  
                    };
                }
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }


        [HttpGet]
        [ActionName("GetEmployeeBenefit")]
        public VivifyResponse<List<EmployeeBenefitResponse>> GetEmployeeBenefit(long empNo)
        {
            try
            {
               
                var employeeBenefits = CommonDBContext.EmployeeBenefits
          .Where(eb => eb.EmpId == empNo && eb.IsActive == true)
          .ToList();


                if (employeeBenefits == null || !employeeBenefits.Any())
                {
                    return new VivifyResponse<List<EmployeeBenefitResponse>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active employee benefits found for the provided EmpNo.",
                        Result = null
                    };
                }

                var responseList = new List<EmployeeBenefitResponse>();

                foreach (var employeeBenefit in employeeBenefits)
                {
                    var payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentId == employeeBenefit.PayComponentId);

                    if (payComponent != null)
                    {
                        responseList.Add(new EmployeeBenefitResponse
                        {
                            EmpID = employeeBenefit.EmpId,
                            PayComponentID = employeeBenefit.PayComponentId,
                            Amount = employeeBenefit.Amount,
                            ChangeReason = employeeBenefit.ChangeReason,
                            ChangeEffectiveDate = employeeBenefit.ChangeEffectiveDate,
                            LastUpdatedBy_EmpId = employeeBenefit.LastUpdatedByEmpId,
                            LastUpdated_Date = employeeBenefit.LastUpdatedDate,
                            PayComponentName = payComponent.PayComponentName
                        });
                    }
                }

                return new VivifyResponse<List<EmployeeBenefitResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Active employee benefits retrieved successfully.",
                    Result = responseList
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<EmployeeBenefitResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("DeleteEmployeeBenefit")]
        public async Task<VivifyResponse<object>> DeleteEmployeeBenefit(
    [FromForm] int EmpId,
    [FromForm] int PayComponentID,
    [FromForm] int IsActive)
        {
            try
            {
               
                if (EmpId == 0 || PayComponentID == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Employee ID and PayComponentID are required.",
                        Result = null
                    };
                }

                var existingBenefit = await CommonDBContext.EmployeeBenefits
                    .FirstOrDefaultAsync(b => b.EmpId == EmpId && b.PayComponentId == PayComponentID);

                if (existingBenefit == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee benefit record not found or already inactive.",
                        Result = null
                    };
                }

             
                if (IsActive == 0)
                {
                    existingBenefit.IsActive = false;
                    existingBenefit.LastUpdatedByEmpId = EmpId;
                    existingBenefit.LastUpdatedDate = DateTime.Now;

                    await CommonDBContext.SaveChangesAsync();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Employee benefit deleted successfully.",
                        Result = new { BenefitID = existingBenefit.BenefitId }
                    };
                }
                else
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid 'IsActive' value. Use 0 for soft delete.",
                        Result = null
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("EditEmployeeBenefit")]
        public VivifyResponse<EmployeeBenefitResponse> EditEmployeeBenefit(long empNo, int payComponentId)
        {
            try
            {
               
                var employeeBenefit = CommonDBContext.EmployeeBenefits
                    .FirstOrDefault(eb => eb.EmpId == empNo && eb.PayComponentId == payComponentId);

                if (employeeBenefit == null)
                {
                    return new VivifyResponse<EmployeeBenefitResponse>
                    {
                        StatusCode = 404,
                        StatusDesc = "No matching employee benefit found for the provided EmpNo and PayComponentID.",
                        Result = null
                    };
                }

               
                var payComponent = CommonDBContext.PayComponents
                    .FirstOrDefault(pc => pc.PayComponentId == employeeBenefit.PayComponentId);

                var response = new EmployeeBenefitResponse
                {
                    EmpID = employeeBenefit.EmpId,
                    PayComponentID = employeeBenefit.PayComponentId,
                    Amount = employeeBenefit.Amount,
                    ChangeReason = employeeBenefit.ChangeReason,
                    ChangeEffectiveDate = employeeBenefit.ChangeEffectiveDate,
                    LastUpdatedBy_EmpId = employeeBenefit.LastUpdatedByEmpId,
                    LastUpdated_Date = employeeBenefit.LastUpdatedDate,
                    PayComponentName = payComponent?.PayComponentName
                };

                return new VivifyResponse<EmployeeBenefitResponse>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee benefit retrieved successfully.",
                    Result = response
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<EmployeeBenefitResponse>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddEmployeeIncentive")]
        public VivifyResponse<string> AddEmployeeIncentive([FromForm] EmployeeIncentiveRequest request)
        {
            try
            {
               
                long editedByEmpNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value);

              
                var existingIncentive = CommonDBContext.EmployeeIncentives
                    .FirstOrDefault(ei => ei.EmpId == request.EmpID && ei.PayComponentId == request.PayComponentID);

                if (existingIncentive != null)
                {
                    // If it exists, check if the details are different
                    if (existingIncentive.Amount != request.Amount ||
                        existingIncentive.ChangeReason != request.ChangeReason ||
                        existingIncentive.ChangeEffectiveDate != (request.ChangeReason == "Revision" ? request.ChangeEffectiveDate : null))
                    {
                        // If details are different, update the existing incentive
                        existingIncentive.Amount = request.Amount;
                        existingIncentive.ChangeReason = request.ChangeReason;
                        existingIncentive.ChangeEffectiveDate = request.ChangeReason == "Revision" ? request.ChangeEffectiveDate : null;
                        existingIncentive.LastUpdatedByEmpId = (int)editedByEmpNo;
                        existingIncentive.LastUpdatedDate = DateTime.Now;

                        CommonDBContext.EmployeeIncentives.Update(existingIncentive);
                        CommonDBContext.SaveChanges();

                        return new VivifyResponse<string>
                        {
                            StatusCode = 200,
                            StatusDesc = "Employee incentive updated successfully.",
                            Result = $"{existingIncentive.EmpId},{existingIncentive.PayComponentId}"  // Returning EmpID and PayComponentID
                        };
                    }
                    else
                    {
                        // If the details are the same, no need to update
                        return new VivifyResponse<string>
                        {
                            StatusCode = 200,
                            StatusDesc = "Employee incentive already up-to-date.",
                            Result = $"{existingIncentive.EmpId},{existingIncentive.PayComponentId}"  // Returning EmpID and PayComponentID
                        };
                    }
                }
                else
                {
                    // If it doesn't exist, create a new Employee_Incentive record
                    var newIncentive = new EmployeeIncentive
                    {
                        EmpId = request.EmpID,
                        PayComponentId= request.PayComponentID,
                        Amount = request.Amount,
                        ChangeReason = request.ChangeReason,
                        ChangeEffectiveDate = request.ChangeReason == "Revision" ? request.ChangeEffectiveDate : null,
                        IsActive = true,
                        LastUpdatedByEmpId = (int)editedByEmpNo,
                        LastUpdatedDate = DateTime.Now
                    };

                    CommonDBContext.EmployeeIncentives.Add(newIncentive);
                    CommonDBContext.SaveChanges();

                    return new VivifyResponse<string>
                    {
                        StatusCode = 200,
                        StatusDesc = "Employee incentive added successfully.",
                        Result = $"{newIncentive.EmpId},{newIncentive.PayComponentId}"  // Returning EmpID and PayComponentID
                    };
                }
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeIncentive")]
        public VivifyResponse<List<EmployeeIncentiveResponse>> GetEmployeeIncentive(long empNo)
        {
            try
            {
                // Fetch only active incentives for the given EmpNo
                var employeeIncentives = CommonDBContext.EmployeeIncentives
                 .Where(ei => ei.EmpId == empNo && ei.IsActive == true)

                    .ToList();

                if (employeeIncentives == null || !employeeIncentives.Any())
                {
                    return new VivifyResponse<List<EmployeeIncentiveResponse>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active employee incentives found for the provided EmpNo.",
                        Result = null
                    };
                }

                // Prepare response list
                var responseList = new List<EmployeeIncentiveResponse>();

                foreach (var incentive in employeeIncentives)
                {
                    // Get PayComponentName from PayComponent table
                    var payComponent = CommonDBContext.PayComponents
                        .FirstOrDefault(pc => pc.PayComponentId == incentive.PayComponentId);

                    if (payComponent != null)
                    {
                        responseList.Add(new EmployeeIncentiveResponse
                        {
                            EmpID = incentive.EmpId,
                            PayComponentID = incentive.PayComponentId,
                            Amount = incentive.Amount,
                            ChangeReason = incentive.ChangeReason,
                            ChangeEffectiveDate = incentive.ChangeEffectiveDate,
                            LastUpdatedBy_EmpId = incentive.LastUpdatedByEmpId,
                            LastUpdated_Date = incentive.LastUpdatedDate,
                            PayComponentName = payComponent.PayComponentName
                        });
                    }
                }

                return new VivifyResponse<List<EmployeeIncentiveResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Active employee incentives retrieved successfully.",
                    Result = responseList
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<EmployeeIncentiveResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("EditEmployeeIncentive")]
        public VivifyResponse<EmployeeIncentiveResponse> EditEmployeeIncentive(long empNo, int payComponentId)
        {
            try
            {
                // Fetch the specific employee incentive for the given EmpID and PayComponentID
                var employeeIncentive = CommonDBContext.EmployeeIncentives
                    .FirstOrDefault(ei => ei.EmpId == empNo && ei.PayComponentId == payComponentId);

                if (employeeIncentive == null)
                {
                    return new VivifyResponse<EmployeeIncentiveResponse>
                    {
                        StatusCode = 404,
                        StatusDesc = "No matching employee incentive found for the provided EmpNo and PayComponentID.",
                        Result = null
                    };
                }

                // Fetch the PayComponentName
                var payComponent = CommonDBContext.PayComponents
                    .FirstOrDefault(pc => pc.PayComponentId == employeeIncentive.PayComponentId);

                var response = new EmployeeIncentiveResponse
                {
                    EmpID = employeeIncentive.EmpId,
                    PayComponentID = employeeIncentive.PayComponentId,
                    Amount = employeeIncentive.Amount,
                    ChangeReason = employeeIncentive.ChangeReason,
                    ChangeEffectiveDate = employeeIncentive.ChangeEffectiveDate,
                    LastUpdatedBy_EmpId = employeeIncentive.LastUpdatedByEmpId,
                    LastUpdated_Date = employeeIncentive.LastUpdatedDate,
                    PayComponentName = payComponent?.PayComponentName
                };

                return new VivifyResponse<EmployeeIncentiveResponse>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee incentive retrieved successfully.",
                    Result = response
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<EmployeeIncentiveResponse>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("DeleteEmployeeIncentive")]
        public async Task<VivifyResponse<object>> DeleteEmployeeIncentive(
        [FromForm] int EmpId,
        [FromForm] int PayComponentID,
        [FromForm] int IsActive)
        {
            try
            {
                if (EmpId == 0 || PayComponentID == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Employee ID (EmpId) and PayComponentID are required.",
                        Result = null
                    };
                }

                var existingIncentive = await CommonDBContext.EmployeeIncentives
                    .FirstOrDefaultAsync(i => i.EmpId == EmpId && i.PayComponentId == PayComponentID);

                if (existingIncentive == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee incentive record not found.",
                        Result = null
                    };
                }

                if (IsActive == 0) // Soft delete if IsActive is 0
                {
                    existingIncentive.IsActive = false;
                    existingIncentive.LastUpdatedByEmpId = EmpId;
                    existingIncentive.LastUpdatedDate = DateTime.Now;

                    await CommonDBContext.SaveChangesAsync();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Employee incentive deleted successfully.",
                        Result = new
                        {
                            IncentiveID = existingIncentive.IncentiveId
                        }
                    };
                }
                else
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid 'IsActive' value. It should be 0 for soft delete.",
                        Result = null
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }


        [HttpGet]
        [ActionName("GetReasonTypes")]
        public VivifyResponse<List<ReasonTypeResponse>> GetReasonTypes()
        {
            try
            {
                var reasonTypes = CommonDBContext.EmployeeReasons
                    .Where(x => x.IsActive == true)
                    .Select(x => new ReasonTypeResponse
                    {
                        ReasonId = x.ReasonId,
                        ReasonDescription = x.ReasonDesc,
                        IsActive = x.IsActive
                    })
                    .ToList();

                return new VivifyResponse<List<ReasonTypeResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Reason types fetched successfully.",
                    Result = reasonTypes
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<ReasonTypeResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<ReasonTypeResponse>()
                };
            }
        }
        [HttpGet]
        [ActionName("GetSmithTeamLeaders")]
        public VivifyResponse<TeamLeaderResponseDto> GetSmithTeamLeaders() 
        {
            try
            {
                string branchCode = Convert.ToString(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Branch")?.Value);

                if (string.IsNullOrEmpty(branchCode))
                {
                    return new VivifyResponse<TeamLeaderResponseDto>
                    {
                        StatusCode = 400,
                        StatusDesc = "Branch code not found in token"
                    };
                }

                var teamLeaders = CommonDBContext.SmithTeamLeaders
       .Where(x => x.BranchId != null && x.BranchId == branchCode && x.IsActive)
       .Select(x => new TeamLeaderDto
       {
           LeaderID = x.LeaderId,
           FullName = x.FullName ?? string.Empty,
           BranchId = x.BranchId ?? string.Empty,
           RegionID = x.RegionId,
           Email = x.Email ?? string.Empty
       })
       .ToList();


                if (!teamLeaders.Any())
                {
                    return new VivifyResponse<TeamLeaderResponseDto>
                    {
                        StatusCode = 404,
                        StatusDesc = "No team leaders found for the specified branch"
                    };
                }

                return new VivifyResponse<TeamLeaderResponseDto>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = new TeamLeaderResponseDto
                    {
                        BranchId = branchCode,
                        TeamLeaders = teamLeaders
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<TeamLeaderResponseDto>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }


        [HttpGet]
        [ActionName("GetRegionTeamLeaders")]
        [Authorize]
        public VivifyResponse<RegionMailResponseDto> GetRegionTeamLeaders()
        {
            try
            {
                // 1. Extract BranchCode from JWT claims
                string branchCode = Convert.ToString(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Branch")?.Value);

                if (string.IsNullOrEmpty(branchCode))
                {
                    return new VivifyResponse<RegionMailResponseDto>
                    {
                        StatusCode = 400,
                        StatusDesc = "Branch code not found in token",
                        Result = null
                    };
                }

                // 2. Get RegionID for this branch
                var regionInfo = CommonDBContext.MBranches
                    .Where(b => b.BranchCode == branchCode && b.IsActive)
                    .Select(b => new {
                        b.RegionId
                    })
                    .FirstOrDefault();

                if (regionInfo == null)
                {
                    return new VivifyResponse<RegionMailResponseDto>
                    {
                        StatusCode = 404,
                        StatusDesc = $"No region found for branch {branchCode}",
                        Result = null
                    };
                }

              
                var regionMail = CommonDBContext.MRegions
                    .Where(r => r.RegionID == regionInfo.RegionId )
                    .Select(r => new RegionMailDto
                    {
                        RegionID = r.RegionID,
                        RegionDesc = r.RegionDesc,
                        MailID = r.MailId,  
                        BranchCode = branchCode
                    })
                    .FirstOrDefault();

                if (regionMail == null)
                {
                    return new VivifyResponse<RegionMailResponseDto>
                    {
                        StatusCode = 404,
                        StatusDesc = $"No mail details found for region {regionInfo.RegionId}",
                        Result = null
                    };
                }

                return new VivifyResponse<RegionMailResponseDto>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = new RegionMailResponseDto
                    {
                        RegionMail = regionMail
                    }
                };
            }
            catch (Exception ex)
            {
                // Simplified error message for production
                return new VivifyResponse<RegionMailResponseDto>
                {
                    StatusCode = 500,
                    StatusDesc = "Internal server error",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetSiteTeamLeaders")]
        public VivifyResponse<LineManagerResponseDto> GetSiteTeamLeaders()
        {
            try
            {
                string branchCode = Convert.ToString(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "Branch")?.Value);

                if (string.IsNullOrEmpty(branchCode))
                {
                    return new VivifyResponse<LineManagerResponseDto>
                    {
                        StatusCode = 400,
                        StatusDesc = "Branch code not found in token"
                    };
                }

                var lineManagers = CommonDBContext.LineManagers
                    .Where(x => x.BranchId != null && x.BranchId == branchCode)
                    .Select(x => new LineManagerDto
                    {
                        LineMgrID = x.LineMgrId,
                        LineManager = x.LineManager1,
                        EmployeeID = x.EmployeeID,
                        MailID = x.MailId ?? string.Empty,
                        BranchCode = x.BranchId
                    })
                    .ToList();

                if (!lineManagers.Any())
                {
                    return new VivifyResponse<LineManagerResponseDto>
                    {
                        StatusCode = 404,
                        StatusDesc = "No line managers found for the specified branch"
                    };
                }

                return new VivifyResponse<LineManagerResponseDto>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = new LineManagerResponseDto
                    {
                        BranchCode = branchCode,
                        LineManagers = lineManagers
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<LineManagerResponseDto>
                {
                    StatusCode = 500,
                    StatusDesc = ex.Message + ":" + ex.StackTrace
                };
            }
        }

        [HttpPost]
        [ActionName("AddInductionInfo")]
        public async Task<VivifyResponse<object>> AddInductionInfo([FromForm] InductionInfoRequest request)
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);

                bool isActive = request.IsActive ?? true;

                if (request.InductionId > 0)
                {
                    var existing = await CommonDBContext.InductionInfos
                        .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == request.InductionId);

                    if (existing != null)
                    {
                        existing.InductionName = !string.IsNullOrWhiteSpace(request.InductionName)
                            ? request.InductionName
                            : existing.InductionName;

                        if (request.VideoDocumentFile != null && request.VideoDocumentFile.Length > 0)
                        {
                            string filePath = await SaveAttachmentFileAsync(request.VideoDocumentFile);
                            existing.VideoPath = filePath;

                            var duration = await GetVideoDurationAsync(request.VideoDocumentFile);
                            existing.Duration = duration;
                        }

                        existing.IsActive = isActive;
                        existing.UpdBy = (int)empNo;
                        existing.UpdDate = DateOnly.FromDateTime(DateTime.Now);

                        await CommonDBContext.SaveChangesAsync();

                        return new VivifyResponse<object>
                        {
                            StatusCode = 200,
                            StatusDesc = "Induction info updated successfully.",
                            Result = new { InductionId = existing.Id }
                        };
                    }

                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Induction record not found for the provided ID.",
                        Result = null
                    };
                }

                if (request.VideoDocumentFile == null || request.VideoDocumentFile.Length == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Video file is required for new induction.",
                        Result = null
                    };
                }

                string newFilePath = await SaveAttachmentFileAsync(request.VideoDocumentFile);
                var newDuration = await GetVideoDurationAsync(request.VideoDocumentFile);

                var newInduction = new InductionInfo
                {
                    CompanyId = companyId,
                    InductionName = request.InductionName,
                    VideoPath = newFilePath,
                    Duration = newDuration,
                    IsActive = isActive,
                    UpdBy = (int)empNo,
                    UpdDate = DateOnly.FromDateTime(DateTime.Now)
                };

                CommonDBContext.InductionInfos.Add(newInduction);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Induction video uploaded successfully.",
                    Result = new { EmpId = newInduction.UpdBy, InductionId = newInduction.Id }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        private async Task<decimal?> GetVideoDurationAsync(IFormFile videoFile)
        {
            var tempFilePath = Path.GetTempFileName();

            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }

                var inputFile = new MediaToolkit.Model.MediaFile { Filename = tempFilePath };

                using (var engine = new MediaToolkit.Engine())
                {
                    try
                    {
                        engine.GetMetadata(inputFile);
                    }
                    catch (Exception ex)
                    {
                        // Log error if necessary
                        return null; // Fail gracefully
                    }
                }

                if (inputFile.Metadata == null || inputFile.Metadata.Duration == TimeSpan.Zero)
                {
                    return null; 
                }

                var duration = inputFile.Metadata.Duration;

                int minutes = duration.Minutes;
                int seconds = duration.Seconds;

             
                string formatted = $"{minutes}.{seconds:D2}";

                if (decimal.TryParse(formatted, out decimal result))
                {
                    return result;
                }

                return null;
            }
            finally
            {
                if (System.IO.File.Exists(tempFilePath))
                {
                    System.IO.File.Delete(tempFilePath);
                }
            }
        }



        [HttpGet]
        [ActionName("EditInductionInfo")]
        public async Task<VivifyResponse<object>> EditInductionInfo([FromQuery] int inductionId)
        {
            try
            {
               
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var induction = await CommonDBContext.InductionInfos
                    .Where(i => i.CompanyId == companyId && i.IsActive == true && i.Id == inductionId) // Filter by IsActive and inductionId
                    .Select(i => new
                    {
                        i.Id,
                        i.InductionName,
                        i.Duration,
                        i.VideoPath,
                        i.IsActive,
                        i.UpdBy,
                        i.UpdDate
                    })
                    .FirstOrDefaultAsync(); // Use FirstOrDefaultAsync to get the specific induction

                if (induction == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Induction info not found or inactive.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Induction info fetched successfully.",
                    Result = induction
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("DeleteInductionInfo")]
        public async Task<VivifyResponse<object>> DeleteInductionInfo([FromBody] DeleteInductionRequest request)
        {
            try
            {
              
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);

                // Check if the request has a valid InductionId and IsActive status
                if (request.InductionId <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid InductionId provided.",
                        Result = null
                    };
                }

                // Get the Induction record to be deleted or updated
                var induction = await CommonDBContext.InductionInfos
                    .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Id == request.InductionId);

                if (induction == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Induction record not found.",
                        Result = null
                    };
                }

                // Perform soft delete (mark as inactive) if IsActive is false
                if (!request.IsActive)
                {
                    induction.IsActive = false;
                    induction.UpdBy = (int)empNo;
                    induction.UpdDate = DateOnly.FromDateTime(DateTime.Now);
                    await CommonDBContext.SaveChangesAsync();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Induction info deleted successfully.",
                        Result = new { InductionId = induction.Id }
                    };
                }

             
                return new VivifyResponse<object>
                {
                    StatusCode = 400,
                    StatusDesc = "IsActive must be false for soft delete action.",
                    Result = null
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmployeeInductionInfo")]
        public async Task<VivifyResponse<object>> GetEmployeeInductionInfo()
        {
            try
            {
                var companyClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value;

                if (!int.TryParse(companyClaim, out int companyId))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing CompanyID in token.",
                        Result = null
                    };
                }

                var inductions = await (
    from induction in CommonDBContext.InductionInfos
    join emp in CommonDBContext.EmployeeInfos
        on induction.UpdBy equals (int?)emp.EmpNo into empJoin
    from emp in empJoin.DefaultIfEmpty()
    where induction.CompanyId == companyId && induction.IsActive == true
    select new
    {induction.Id,
        induction.InductionName,
        induction.VideoPath,
        induction.Duration,
        induction.UpdDate,
        Upd_ByName = emp != null ? emp.FirstName : "N/A",
        Status = 1
    }).ToListAsync();

                if (!inductions.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active inductions found for this company.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "All induction info fetched successfully.",
                    Result = inductions
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetInductionInfo")]
        public async Task<VivifyResponse<object>> GetInductionInfo()
        {
            try
            {
                // Extract CompanyID from JWT Token
                var companyClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value;

                if (!int.TryParse(companyClaim, out int companyId))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing CompanyID in token.",
                        Result = null
                    };
                }

                // Get all active inductions for this company with only ID and Name
                var inductions = await CommonDBContext.InductionInfos
                    .Where(i => i.CompanyId == companyId && i.IsActive == true)
                    .Select(i => new
                    {
                        InductionID = i.Id,         // Use Id from the model
                        InductionName = i.InductionName
                    })
                    .ToListAsync();

                if (!inductions.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active inductions found for this company.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "All induction info fetched successfully.",
                    Result = inductions
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("AddPolicyInfo")]
        public async Task<VivifyResponse<object>> AddPolicyInfo([FromForm] PolicyInfoRequest request)
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value);

                if (string.IsNullOrWhiteSpace(request.PolicyTitle))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Policy title is required.",
                        Result = null
                    };
                }

                if (string.IsNullOrWhiteSpace(request.PolicyContent))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Policy content is required.",
                        Result = null
                    };
                }

                if (request.PolicyDocumentFile == null || request.PolicyDocumentFile.Length == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Policy document file is required.",
                        Result = null
                    };
                }

                string filePath = await SaveAttachmentFileAsync(request.PolicyDocumentFile); // Implement file saving

                var policy = new Policy
                {
                    CompanyId = companyId,
                    PolicyTitle = request.PolicyTitle,
                    PolicyContent = request.PolicyContent,
                    PolicyDocumentPath = filePath,
                    IsActive = request.IsActive ?? true,
                    UpdBy = (int)empNo,
                    UpdDate = DateTime.Now
                };

                CommonDBContext.Policies.Add(policy);
                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Policy added successfully.",
                    Result = new { PolicyID = policy.PolicyId }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetPolicyInfo")]
        public async Task<VivifyResponse<object>> GetPolicyInfo()
        {
            try
            {
                // Safely get claims with null checks
                var companyIdClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID");
                var empNoClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo");

                if (companyIdClaim == null || empNoClaim == null ||
                    !int.TryParse(companyIdClaim.Value, out int companyId) ||
                    !long.TryParse(empNoClaim.Value, out long empNo))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 401,
                        StatusDesc = "Invalid user claims.",
                        Result = null
                    };
                }

               
                var policies = await CommonDBContext.Policies
      .Where(policy => policy.CompanyId == companyId && policy.IsActive == true)
      .Where(policy => !CommonDBContext.EmployeePolicies
          .Any(ep =>
              ep.CompanyId == companyId &&
              ep.EmpId == empNo &&
            
              ep.Status == 1 &&
              (ep.DocumentTypeId != null || ep.DocumentTypeId == 12)
          ))
      .Select(policy => new
      {
          policy.PolicyTitle,
          policy.PolicyDocumentPath,
          policy.PolicyContent,
          policy.CompanyId,
          policy.IsActive,
          policy.UpdBy,
          policy.UpdDate,
      })
      .ToListAsync();


                if (!policies.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No pending policies found for the employee.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Pending policies fetched successfully.",
                    Result = policies
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
       
        [HttpPost]
        [ActionName("AddEmployeePolicy")]
        public async Task<VivifyResponse<object>> AddEmployeePolicy([FromBody] EmployeePolicyStatusRequest request)
        {
            try
            {
                var companyClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value;
                var empNoClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value;

                if (!int.TryParse(companyClaim, out int companyId) || !long.TryParse(empNoClaim, out long empNo))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing claims in token.",
                        Result = null
                    };
                }

                if (request.Status != 0 && request.Status != 1)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid status value. Only 0 or 1 allowed.",
                        Result = null
                    };
                }

                var existingRecord = await CommonDBContext.EmployeePolicies
                    .FirstOrDefaultAsync(ep => ep.EmpId == empNo && ep.CompanyId == companyId);

                // Block status = 0 if DocumentUpeId == 12
                if (existingRecord != null && existingRecord.DocumentTypeId == 12 && request.Status == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 403,
                        StatusDesc = "Cannot revoke policy status when DocumentUpeId is 12.",
                        Result = null
                    };
                }

                if (existingRecord != null)
                {
                    existingRecord.Status = request.Status;
                    existingRecord.UpdDate = DateTime.Now;
                }
                else
                {
                    CommonDBContext.EmployeePolicies.Add(new EmployeePolicy
                    {
                        EmpId = empNo,
                        CompanyId = companyId,
                        Status = request.Status,
                        UpdDate = DateTime.Now
                    });
                }

                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = request.Status == 1
                        ? "Policy accepted successfully."
                        : "Policy status revoked successfully (set to 0).",
                    Result = null
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }


        [HttpPost]
        [ActionName("AddEmployeeDeduction")]
        public async Task<VivifyResponse<object>> AddEmployeeDeduction([FromForm] EmployeeDeductionRequest request)
        {
            try
            {
                // Check if EmpId is provided in the request body
                if (request.EmpId == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Employee ID (EmpId) is required.",
                        Result = null
                    };
                }

                // Fetch the pay component based on the provided PayComponentID
                var payComponent = await CommonDBContext.PayComponents
                    .FirstOrDefaultAsync(p => p.PayComponentId == request.PayComponentID && p.PayComponentTypeId == 3);

                if (payComponent == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Deduction component not found.",
                        Result = null
                    };
                }

                // Retrieve values from the request
                decimal deductionAmount = request.Advance_DedAmount;
                int repaymentMonths = request.RepaymentMonths;

                decimal monthlyRepayment = 0;

                if (repaymentMonths > 0)
                {
                    monthlyRepayment = Math.Round(deductionAmount / repaymentMonths, 2);
                }
                else
                {
                    // When repaymentMonths is 0, assign full deduction amount
                    monthlyRepayment = deductionAmount;
                }

                if (!DateTime.TryParseExact(request.Repayment_StartMonthYear, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime startDate))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid start month format. Use dd/MM/yyyy.",
                        Result = null
                    };
                }

                DateTime endDate = startDate.AddMonths(repaymentMonths - 1);
                string endMonthYear = endDate.ToString("dd/MM/yyyy");

                // Extract EmpId from JWT token for LastUpdatedBy_EmpId
                var empNoClaim = HttpContext.User.Claims.FirstOrDefault(x => x.Type == "EmpNo")?.Value;
                if (!long.TryParse(empNoClaim, out long empNo))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 401,
                        StatusDesc = "Invalid or missing employee claim from JWT.",
                        Result = null
                    };
                }

                // Check if deduction record exists for the given employee and pay component
                var existingDeduction = await CommonDBContext.EmployeeDeductions
                    .FirstOrDefaultAsync(d => d.EmpId == request.EmpId && d.PayComponentID == request.PayComponentID);

                if (existingDeduction != null)
                {
                    // Update the existing deduction record
                    existingDeduction.Advance_DedAmount = deductionAmount;
                    existingDeduction.RepaymentMonths = repaymentMonths;
                    existingDeduction.Repayment_StartMonthYear = request.Repayment_StartMonthYear;
                    existingDeduction.Repayment_EndMonthYear = endMonthYear;
                    existingDeduction.MonthlyRepayment_Amt = monthlyRepayment;
                    existingDeduction.LastUpdatedByEmpId = (int)empNo;
                    existingDeduction.LastUpdatedDate = DateTime.Now;
                    existingDeduction.Active = 1;
                }
                else
                {
                    // Create a new deduction record if none exists
                    var newDeduction = new EmployeeDeduction
                    {
                        EmpId = request.EmpId,
                        PayComponentID = request.PayComponentID,
                        Advance_DedAmount = deductionAmount,
                        RepaymentMonths = repaymentMonths,
                        Repayment_StartMonthYear = request.Repayment_StartMonthYear,
                        Repayment_EndMonthYear = endMonthYear,
                        MonthsPaid = 0,
                        RepaymentComplete = 0,
                        MonthlyRepayment_Amt = monthlyRepayment,
                        Active = 1,
                        LastUpdatedByEmpId = (int)empNo,
                        LastUpdatedDate = DateTime.Now
                    };

                    CommonDBContext.EmployeeDeductions.Add(newDeduction);
                }

                await CommonDBContext.SaveChangesAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = existingDeduction != null
                        ? "Employee deduction updated successfully."
                        : "Employee deduction added successfully.",
                    Result = new
                    {
                        DeductionID = existingDeduction?.DeductionID
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeDeduction")]
        public async Task<VivifyResponse<object>> GetEmployeeDeduction(int empId)
        {
            try
            {
                var deductions = await CommonDBContext.EmployeeDeductions
                    .Where(d => d.EmpId == empId && d.Active == 1)
                    .Select(d => new
                    {
                        d.DeductionID,
                        d.EmpId,
                        d.PayComponentID,
                        d.Advance_DedAmount,
                        d.RepaymentMonths,
                        d.Repayment_StartMonthYear,
                        d.Repayment_EndMonthYear,
                        d.MonthsPaid,
                        d.RepaymentComplete,
                        MonthlyRepayment_Amt = d.RepaymentMonths.GetValueOrDefault() > 0
    ? Math.Round(d.Advance_DedAmount.GetValueOrDefault() / d.RepaymentMonths.GetValueOrDefault(), 2)
    : d.Advance_DedAmount.GetValueOrDefault(),
                        d.LastUpdatedByEmpId,
                        d.LastUpdatedDate,

                        // Fetch the PayComponentName from the PayComponent table
                        PayComponentName = CommonDBContext.PayComponents
                            .Where(pc => pc.PayComponentId== d.PayComponentID)
                            .Where(pc => pc.PayComponentId== d.PayComponentID)
                            .Select(pc => pc.PayComponentName)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                if (!deductions.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No deduction records found for this employee.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee deductions fetched successfully.",
                    Result = deductions
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("DeleteEmployeeDeduction")]
        public async Task<VivifyResponse<object>> DeleteEmployeeDeduction([FromForm] int EmpId, [FromForm] int PayComponentID, [FromForm] int IsActive)
        {
            try
            {
                // Validate the incoming form data
                if (EmpId == 0 || PayComponentID == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Employee ID (EmpId) and PayComponentID are required.",
                        Result = null
                    };
                }

                // Fetch the deduction record based on EmpId and PayComponentID
                var existingDeduction = await CommonDBContext.EmployeeDeductions
                    .FirstOrDefaultAsync(d => d.EmpId == EmpId && d.PayComponentID== PayComponentID);

                if (existingDeduction == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee deduction record not found.",
                        Result = null
                    };
                }

                // If IsActive is 0, mark the record as inactive (soft delete)
                if (IsActive == 0)
                {
                    existingDeduction.Active = 0;
                    existingDeduction.LastUpdatedByEmpId = EmpId;
                    existingDeduction.LastUpdatedDate = DateTime.Now;

                    // Save changes to the database
                    await CommonDBContext.SaveChangesAsync();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Employee deduction deleted successfully.",
                        Result = new
                        {
                            DeductionID = existingDeduction.DeductionID
                        }
                    };
                }
                else
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid 'IsActive' value. It should be 0 for soft delete.",
                        Result = null
                    };
                }
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        [HttpGet]
        [ActionName("EditEmployeeDeduction")]
        public async Task<VivifyResponse<object>> EditEmployeeDeduction([FromQuery] int EmpId, [FromQuery] int PayComponentID)
        {
            try
            {
                // Validate the incoming query parameters
                if (EmpId == 0 || PayComponentID == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Employee ID (EmpId) and PayComponentID are required.",
                        Result = null
                    };
                }

                // Fetch the deduction record based on EmpId and PayComponentID
                var existingDeduction = await CommonDBContext.EmployeeDeductions
                    .FirstOrDefaultAsync(d => d.EmpId == EmpId && d.PayComponentID == PayComponentID && d.Active == 1);

                if (existingDeduction == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee deduction record not found.",
                        Result = null
                    };
                }

                // Fetch the PayComponentName
                var payComponent = await CommonDBContext.PayComponents
                    .FirstOrDefaultAsync(pc => pc.PayComponentId == PayComponentID);

                // Return the result with PayComponentName included
                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee deduction details fetched successfully.",
                    Result = new
                    {
                        DeductionID = existingDeduction.DeductionID,
                        Advance_DedAmount = existingDeduction.Advance_DedAmount,
                        RepaymentMonths = existingDeduction.RepaymentMonths,
                        Repayment_StartMonthYear = existingDeduction.Repayment_StartMonthYear,
                        Repayment_EndMonthYear = existingDeduction.Repayment_EndMonthYear,
                        MonthlyRepayment_Amt = existingDeduction.MonthlyRepayment_Amt,
                        LastUpdatedBy_EmpId = existingDeduction.LastUpdatedByEmpId,
                        LastUpdated_Date = existingDeduction.LastUpdatedDate,
                        PayComponentID = existingDeduction.PayComponentID,
                        PayComponentName = payComponent?.PayComponentName // Get the PayComponentName
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmployeeResignation")]
        public async Task<VivifyResponse<object>> AddEmployeeResignation(
     [FromForm] int EmpId,
     [FromForm] DateOnly RelievingDate,
     [FromForm] int Reason,
     [FromForm] string HREmail,
     [FromForm] string CCMails = null,
     [FromForm] string Remarks = null) // 👈 New parameter added here
        {
            try
            {
                var empIdClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo");
                var companyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "CompanyID");

                if (empIdClaim == null || companyIdClaim == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 401,
                        StatusDesc = "Unauthorized: Missing token claims.",
                        Result = null
                    };
                }

                int createdByEmpId = int.Parse(empIdClaim.Value);
                int companyId = int.Parse(companyIdClaim.Value);

                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == EmpId);

                if (employee == null || employee.DateOfJoin == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found or DateOfJoining is missing.",
                        Result = null
                    };
                }

                if (string.IsNullOrWhiteSpace(employee.EmailId))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Employee official email not found in record.",
                        Result = null
                    };
                }

                DateOnly dateOfJoining = employee.DateOfJoin.Value;

                if (RelievingDate < dateOfJoining)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Relieving date cannot be before Date of Joining.",
                        Result = null
                    };
                }

                int noticePeriod = employee.NoticePeriod ?? 3;

                // Calculate doj + noticePeriod
                DateOnly dojPlusNotice = dateOfJoining.AddMonths(noticePeriod);

                // CreatedDate = now
                DateOnly createdDate = DateOnly.FromDateTime(DateTime.Now);

                // Calculate createdDate + noticePeriod
                DateOnly createdPlusNotice = createdDate.AddMonths(noticePeriod);

                // Expected relieving date is max of the two
                DateOnly expRelievingDate = dojPlusNotice > createdPlusNotice ? dojPlusNotice : createdPlusNotice;

                var reasonEntity = await CommonDBContext.EmployeeReasons
                    .FirstOrDefaultAsync(r => r.ReasonId == Reason);

                if (reasonEntity == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid ReasonId provided.",
                        Result = null
                    };
                }

                string reasonText = reasonEntity.ReasonDesc;

                var hr = await CommonDBContext.HrEmails
                    .FirstOrDefaultAsync(h => h.Email == HREmail);

                if (hr == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "HR email not found.",
                        Result = null
                    };
                }

                string branchName = GetBranchName(employee.BranchCode);

                var resignation = new EmployeeOffBoarding
                {
                    CompanyId = companyId,
                    EmpId = EmpId,
                    RelievingDate = RelievingDate,
                    ExpRelievingDate = expRelievingDate,
                    ReasonId = Reason,
                    Status = "0",
                    CreatedDate = DateTime.Now,
                    CreatedBy = createdByEmpId,
                    CCMails = CCMails,
                    UpdatedDate = null,
                    UpdatedBy = null,
                    BranchId = employee.BranchCode,
                    EmpRemarks = Remarks // 👈 Assign remarks field here
                };

                CommonDBContext.EmployeeOffBoardings.Add(resignation);
                await CommonDBContext.SaveChangesAsync();

                string employeeName = $"{employee.FirstName} {employee.LastName}".Trim();

                try
                {
                    SendResignationEmail(
                        fromEmail: employee.EmailId,
                        toEmail: hr.Email,
                        ccEmails: CCMails,
                        empId: EmpId,
                        employeeName: employeeName,
                        relievingDate: RelievingDate,
                        expRelievingDate: expRelievingDate,
                        reason: reasonText,
                        branchName: branchName,
                        createdDate: resignation.CreatedDate.Value,
                        remarks: Remarks); // 👈 Pass remarks to email

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Resignation submitted successfully.",
                        Result = new
                        {
                            resignation.RelievingDate,
                            resignation.Status,
                            BranchID = employee.BranchCode,
                            BranchName = branchName,
                            HREmail = hr.Email,
                            EmployeeEmail = employee.EmailId,
                            CcEmails = CCMails,
                            Remarks = resignation.Remarks, // 👈 Include remarks in response
                            EmailSent = true
                        }
                    };
                }
                catch (Exception emailEx)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = $"Resignation recorded but email failed: {emailEx.Message}",
                        Result = new
                        {
                            resignation.RelievingDate,
                            resignation.Status,
                            BranchID = employee.BranchCode,
                            BranchName = branchName,
                            HREmail = hr.Email,
                            EmployeeEmail = employee.EmailId,
                            CcEmails = CCMails,
                            Remarks = resignation.Remarks, 
                            EmailSent = false
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddEmployeeResignation: {ex.Message}");

                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = null
                };
            }
        }

        private string GetBranchName(string branchId)
        {
            var branch = CommonDBContext.MBranches
                .FirstOrDefault(b => b.BranchCode == branchId);

            return branch?.BranchDesc ?? "Unknown Branch";
        }

        private void SendResignationEmail(
     string fromEmail,
     string toEmail,
     string ccEmails,
     int empId,
     string employeeName,
     DateOnly relievingDate,
     DateOnly expRelievingDate,
     string reason,
     string branchName,
     DateTime createdDate,
     string remarks = null)
        {
            try
            {
                MailMessage message = new MailMessage();
                SmtpClient smtp = new SmtpClient();

                message.From = new MailAddress("hr@vivifytec.in", employeeName);
                message.To.Add(new MailAddress(toEmail));

                if (!string.IsNullOrWhiteSpace(ccEmails))
                {
                    foreach (var cc in ccEmails.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        message.CC.Add(cc.Trim());
                    }
                }

                message.Subject = $"Resignation Notice: {employeeName} (EmpID: {empId})";
                message.IsBodyHtml = true;

                string body = $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; }}
        .header {{ color: #2c3e50; font-size: 18px; font-weight: bold; margin-bottom: 20px; }}
        .details {{ margin: 15px 0; border: 1px solid #eee; padding: 15px; border-radius: 5px; }}
        .label {{ font-weight: bold; color: #34495e; min-width: 180px; display: inline-block; }}
        .signature {{ margin-top: 20px; font-style: italic; }}
    </style>
</head>
<body>
    <div class='header'>Employee Resignation Notification</div>
    <p>Dear HR Team,</p>

    <div class='details'>
        <p><span class='label'>Employee ID:</span> {empId}</p>
        <p><span class='label'>Employee Name:</span> {employeeName}</p>
        <p><span class='label'>Branch:</span> {branchName}</p>
        <p><span class='label'>Requested Relieving Date:</span> {createdDate:dd-MMM-yyyy}</p>
        <p><span class='label'>Relieving Date:</span> {relievingDate:dd-MMM-yyyy}</p>
        <p><span class='label'>Actual Relieving Date:</span> {expRelievingDate:dd-MMM-yyyy}</p>
        <p><span class='label'>Reason for Resignation:</span> {reason}</p>
        {(remarks != null ? $"<p><span class='label'>Remarks:</span> {remarks}</p>" : "")} <!-- 👈 Include Remarks -->
    </div>

    <p>Kindly process my resignation and initiate the necessary offboarding procedures.</p>

    <div class='signature'>
        <p>Regards,</p>
        <p>{employeeName}</p>
        <p>{fromEmail}</p>
    </div>
</body>
</html>";

                message.Body = body;

                smtp.Host = "smtp.zoho.com";
                smtp.Port = 587;
                smtp.EnableSsl = true;
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new NetworkCredential("hr@vivifytec.in", "HKHGX0AYtNJs");
                smtp.DeliveryMethod = SmtpDeliveryMethod.Network;

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                smtp.Send(message);
            }
            catch (SmtpException smtpEx)
            {
                throw new Exception($"SMTP Failed: {smtpEx.Message} | Inner: {smtpEx.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send resignation email: {ex.Message}");
            }
        }

        [HttpGet]
        [ActionName("GetHREmails")]
        public async Task<VivifyResponse<object>> GetHREmails()
        {
            try
            {
               
                var hrEmails = await CommonDBContext.HrEmails
                    .Select(email => new HrEmail
                    {
                        Id = email.Id,
                        Email = email.Email,
                        IsActive = email.IsActive,
                        CreatedDate = email.CreatedDate,
                        CreatedByEmpId = email.CreatedByEmpId,
                        LastUpdatedDate = email.LastUpdatedDate,
                        LastUpdatedByEmpId = email.LastUpdatedByEmpId
                    })
                    .Take(1000) // Fetch top 1000 records, adjust as needed
                    .ToListAsync();

                // If no records are found
                if (!hrEmails.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No HR Email records found.",
                        Result = null
                    };
                }

                // Return success response with the HR emails data
                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "HR Email records fetched successfully.",
                    Result = hrEmails
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmployeeResignation")]
        public async Task<VivifyResponse<object>> GetEmployeeResignationWithReason([FromQuery] int EmpId)
        {
            try
            {
                var resignation = await CommonDBContext.EmployeeOffBoardings
                    .FirstOrDefaultAsync(r => r.EmpId == EmpId);

                if (resignation == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No resignation record found for the provided EmpId.",
                        Result = null
                    };
                }

                // Get Reason Description
                var reason = await CommonDBContext.EmployeeReasons
                    .FirstOrDefaultAsync(r => r.ReasonId == resignation.ReasonId);
                string reasonDesc = reason?.ReasonDesc ?? "Reason not found";

                // Get Active HR Email
                var hrEmail = await CommonDBContext.HrEmails
                    .Where(h => h.IsActive == true)
                    .Select(h => h.Email)
                    .FirstOrDefaultAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee resignation details fetched successfully.",
                    Result = new
                    {
                        resignation.EmpId,
                        resignation.CompanyId,
                        resignation.BranchId,
                        resignation.RelievingDate,
                        ExpectedRelievingDate = resignation.ExpRelievingDate,
                        resignation.Status,
                        resignation.UpdatedBy, // Admin or approver ID
                        ReasonDesc = reasonDesc,
                        Remarks = resignation.Remarks,
                        CcEmails = resignation.CCMails,
                        resignation.EmpRemarks,
                        HrName = hrEmail
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.InnerException?.Message ?? ex.Message}",
                    Result = null
                };
            }
        }


        [HttpGet]
        [ActionName("GetEmpPendingResignation")]
        public async Task<VivifyResponse<object>> GetEmployeeResignationWithReason(
     [FromQuery] int? EmpId = null,
     [FromQuery] string BranchId = null,
     [FromQuery] string Status = null
 )
        {
            try
            {
                var query = CommonDBContext.EmployeeOffBoardings.AsQueryable();

                
                if (!string.IsNullOrEmpty(Status))
                {
                    query = query.Where(r => r.Status == Status);
                }
                else
                {
                    query = query.Where(r => r.Status == "0" || r.Status == "1");
                }

                if (EmpId.HasValue)
                {
                    query = query.Where(r => r.EmpId == EmpId.Value);
                }

                if (!string.IsNullOrEmpty(BranchId))
                {
                    query = query.Where(r => r.BranchId == BranchId);
                }

                var resignations = await query.ToListAsync();

                if (resignations == null || resignations.Count == 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No resignation records found for the provided filters.",
                        Result = null
                    };
                }

                var resultList = new List<object>();

                foreach (var resignation in resignations)
                {
                    var reason = await CommonDBContext.EmployeeReasons
                        .FirstOrDefaultAsync(r => r.ReasonId == resignation.ReasonId);
                    string reasonDesc = reason?.ReasonDesc ?? "Reason not found";

                    var employee = await CommonDBContext.EmployeeInfos
                        .FirstOrDefaultAsync(e => e.EmpNo == resignation.EmpId);
                    string empName = employee != null ? $"{employee.EmpNo}-{employee.FirstName}" : "Unknown Employee";

                    var branch = await CommonDBContext.MBranches
                        .FirstOrDefaultAsync(b => b.BranchCode == resignation.BranchId);
                    string branchName = branch?.BranchDesc ?? "Unknown Branch";

                    resultList.Add(new
                    {
                        EmpDetails = empName,
                        resignation.RelievingDate,
                        resignation.ExpRelievingDate,
                        resignation.Status, // ✅ Include status in result
                        resignation.EmpRemarks,
                        BranchName = branchName,
                        ReasonDesc = reasonDesc
                    });
                }

                // Get active HR email
                var hrEmail = await CommonDBContext.HrEmails
                    .Where(h => h.IsActive == true)
                    .Select(h => h.Email)
                    .FirstOrDefaultAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Resignation details fetched successfully.",
                    Result = new
                    {
                        hrName = hrEmail,
                        Resignations = resultList
                    }
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.InnerException?.Message ?? ex.Message}",
                    Result = null
                };
            }
        }

        [HttpPost]
        [ActionName("AddEmpResignationStatus")]
        public async Task<VivifyResponse<string>> AddEmpResignationStatus(
      [FromForm] int empId,
      [FromForm] DateOnly? expRelievingDate,
      [FromForm] string? remarks 
  )
        {
            try
            {
                if (empId <= 0)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid EmpId.",
                        Result = null
                    };
                }

                var empNoClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value;
                if (string.IsNullOrEmpty(empNoClaim))
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 401,
                        StatusDesc = "Unauthorized: EmpNo not found in token.",
                        Result = null
                    };
                }

                int editorEmpNo = Convert.ToInt32(empNoClaim);

                var resignation = await CommonDBContext.EmployeeOffBoardings
                    .FirstOrDefaultAsync(r => r.EmpId == empId && r.Status == "0");

                if (resignation == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "No pending resignation record found for the provided EmpId.",
                        Result = null
                    };
                }

                resignation.Status = "1";
                resignation.UpdatedBy = editorEmpNo;
                resignation.UpdatedDate = DateTime.Now;

                // ✅ Optional: update expected relieving date
                if (expRelievingDate.HasValue)
                {
                    resignation.ExpRelievingDate = expRelievingDate.Value;
                }

                // ✅ Optional: update remarks if provided
                if (!string.IsNullOrWhiteSpace(remarks))
                {
                    resignation.Remarks = remarks;
                }

                await CommonDBContext.SaveChangesAsync();

                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == empId);

                if (employee == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = null
                    };
                }

                string branchName = await CommonDBContext.MBranches
                    .Where(b => b.BranchCode == employee.BranchCode)
                    .Select(b => b.BranchDesc)
                    .FirstOrDefaultAsync() ?? "Unknown Branch";

                string employeeName = $"{employee.FirstName} {employee.LastName}".Trim();
                string toEmail = employee.EmailId;
                string ccEmails = resignation.CCMails;

                var reason = await CommonDBContext.EmployeeReasons
                    .FirstOrDefaultAsync(r => r.ReasonId == resignation.ReasonId);

                if (!string.IsNullOrWhiteSpace(toEmail))
                {
                    try
                    {
                        SendApprovalEmail(
                            fromEmail: "hr@vivifytec.in",
                            toEmail: toEmail,
                            ccEmails: ccEmails,
                            empId: empId,
                            employeeName: employeeName,
                            relievingDate: resignation?.RelievingDate,
                            expRelievingDate: resignation.ExpRelievingDate,
                            reason: reason?.ReasonDesc ?? "N/A",
                            branchName: branchName,
                            updatedDate: resignation.UpdatedDate.Value,
                            remarks: resignation.Remarks
                        );

                        return new VivifyResponse<string>
                        {
                            StatusCode = 200,
                            StatusDesc = "Resignation approved and email sent.",
                            Result = "Success"
                        };
                    }
                    catch (Exception ex)
                    {
                        return new VivifyResponse<string>
                        {
                            StatusCode = 200,
                            StatusDesc = $"Resignation approved but email failed: {ex.Message}",
                            Result = "PartialSuccess"
                        };
                    }
                }

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Resignation approved (no email sent - employee email missing).",
                    Result = "Approved"
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = null
                };
            }
        }


        private void SendApprovalEmail(
       string fromEmail,
       string toEmail,
       string ccEmails,
       int empId,
       string employeeName,
       DateOnly? relievingDate,
       DateOnly? expRelievingDate,
       string reason,
       string branchName,
       DateTime updatedDate,
       string? remarks // ✅ New
   )
        {
            try
            {
                MailMessage message = new MailMessage();
                SmtpClient smtp = new SmtpClient();

                message.From = new MailAddress(fromEmail, "HR Department");
                message.To.Add(new MailAddress(toEmail));

                if (!string.IsNullOrWhiteSpace(ccEmails))
                {
                    foreach (var cc in ccEmails.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        message.CC.Add(cc.Trim());
                    }
                }

                message.Subject = $"Resignation Approved: {employeeName} (EmpID: {empId})";
                message.IsBodyHtml = true;

                string body = $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; }}
        .header {{ color: #2c3e50; font-size: 18px; font-weight: bold; margin-bottom: 20px; }}
        .details {{ margin: 15px 0; border: 1px solid #eee; padding: 15px; border-radius: 5px; }}
        .label {{ font-weight: bold; color: #34495e; min-width: 180px; display: inline-block; }}
        .signature {{ margin-top: 20px; font-style: italic; }}
    </style>
</head>
<body>
    <div class='header'>Resignation Approval Notification</div>
    <p>Dear {employeeName},</p>

    <div class='details'>
        <p><span class='label'>Employee ID:</span> {empId}</p>
        <p><span class='label'>Branch:</span> {branchName}</p>
        <p><span class='label'>Resignation Approved On:</span> {updatedDate:dd-MMM-yyyy}</p>
        <p><span class='label'>Requested Relieving Date:</span> {relievingDate:dd-MMM-yyyy}</p>
        <p><span class='label'>Final Relieving Date:</span> {expRelievingDate:dd-MMM-yyyy}</p>
        <p><span class='label'>Reason:</span> {reason}</p>
        {(string.IsNullOrWhiteSpace(remarks) ? "" : $"<p><span class='label'>Remarks:</span> {remarks}</p>")}
    </div>

    <p>Your resignation has been formally approved. Please ensure to complete all offboarding activities and handover processes.</p>

    <div class='signature'>
        <p>Best regards,</p>
        <p>HR Department</p>
        <p>hr@vivifytec.in</p>
    </div>
</body>
</html>";

                message.Body = body;

                smtp.Host = "smtp.zoho.com";
                smtp.Port = 587;
                smtp.EnableSsl = true;
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new NetworkCredential("hr@vivifytec.in", "HKHGX0AYtNJs");
                smtp.DeliveryMethod = SmtpDeliveryMethod.Network;

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                smtp.Send(message);
            }
            catch (SmtpException smtpEx)
            {
                throw new Exception($"SMTP Failed: {smtpEx.Message} | Inner: {smtpEx.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send approval email: {ex.Message}");
            }
        }



        [HttpGet]
        [ActionName("GetAssetTypes")]
        public VivifyResponse<List<AssetTypeResponse>> GetAssetTypes()
        {
            try
            {
                // Retrieve all active asset types
                var assetTypes = CommonDBContext.AssetTypes
                    .Where(x => x.IsActive)
                    .Select(x => new AssetTypeResponse
                    {
                        Id = x.Id,
                        Type = x.Type
                    })
                    .ToList();

                return new VivifyResponse<List<AssetTypeResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Asset types fetched successfully.",
                    Result = assetTypes
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<List<AssetTypeResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<AssetTypeResponse>()
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmployeeAsset")]
        public async Task<VivifyResponse<object>> AddEmployeeAsset(
    [FromForm] int EmpId,
    [FromForm] int AssetsId,
    [FromForm] bool ReturningStatus,
    [FromForm] IFormFile? DocumentFile)
        {
            try
            {
                var empNoClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo");
                var companyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "CompanyID");

                if (empNoClaim == null || companyIdClaim == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 401,
                        StatusDesc = "Unauthorized: Missing token claims.",
                        Result = null
                    };
                }

                int createdBy = int.Parse(empNoClaim.Value);
                int companyId = int.Parse(companyIdClaim.Value);

                string? docPath = null;

                if (DocumentFile != null && DocumentFile.Length > 0)
                {
                    docPath = await SaveAttachmentFileAsync(DocumentFile);
                }

                // 🔍 Check if record already exists for this employee and asset
                var existingEntry = await CommonDBContext.Employee_Asset
                    .FirstOrDefaultAsync(e => e.EmpId == EmpId && e.AssetsId == AssetsId && e.IsActive);

                if (existingEntry != null)
                {
                    // 🔁 Update existing record
                    existingEntry.ReturningStatus = ReturningStatus;

                    if (DocumentFile != null && DocumentFile.Length > 0)
                    {
                        existingEntry.DocPath = docPath; // New document uploaded
                    }
                    else if (DocumentFile == null)
                    {
                        existingEntry.DocPath = null; // File not provided => remove existing document
                    }

                    existingEntry.UpdatedDate = DateTime.Now;
                    existingEntry.UpdatedBy = createdBy;


                    await CommonDBContext.SaveChangesAsync();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Employee asset record updated successfully.",
                        Result = new
                        {
                            existingEntry.Id,
                            existingEntry.EmpId,
                            existingEntry.AssetsId,
                            existingEntry.ReturningStatus,
                            existingEntry.DocPath
                        }
                    };
                }
                else
                {
                    // ➕ Create new record
                    var newEntry = new Employee_Assets
                    {
                        EmpId = EmpId,
                        CompanyId = companyId,
                        AssetsId = AssetsId,
                        ReturningStatus = ReturningStatus,
                        IsActive = true,
                        Verified = false,
                        CreatedDate = DateTime.Now,
                        CreatedBy = createdBy,
                        DocPath = docPath,
                        UpdatedDate = null,
                        UpdatedBy = null
                    };

                    CommonDBContext.Employee_Asset.Add(newEntry);
                    await CommonDBContext.SaveChangesAsync();

                    return new VivifyResponse<object>
                    {
                        StatusCode = 200,
                        StatusDesc = "Employee asset record added successfully.",
                        Result = new
                        {
                            newEntry.Id,
                            newEntry.EmpId,
                            newEntry.AssetsId,
                            newEntry.ReturningStatus,
                            newEntry.DocPath
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                var errorDetails = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {errorDetails}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmpAssetReturn")]
        public async Task<VivifyResponse<object>> AddEmpAssetReturn(
      [FromBody] EmployeeAssetUpdateRequestDto request)
        {
            try
            {
                var empNoClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo");

                if (empNoClaim == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 401,
                        StatusDesc = "Unauthorized: Missing token claims.",
                        Result = null
                    };
                }

                int updatedBy = int.Parse(empNoClaim.Value);

                // Validate input
                if (request.Empid <= 0 || request.Assests == null || !request.Assests.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid input: EmpId or asset list missing.",
                        Result = null
                    };
                }

                // Get all asset IDs from request
                var assetIds = request.Assests.Select(a => a.Assetsid).ToList();

                // Fetch matching active assets for this employee
                var dbAssets = await CommonDBContext.Employee_Asset
                    .Where(ea =>
                        ea.EmpId == request.Empid &&
                        assetIds.Contains(ea.AssetsId) &&
                        ea.IsActive)
                    .ToListAsync();

                if (!dbAssets.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No matching active asset records found.",
                        Result = null
                    };
                }

                // Update each asset individually
                foreach (var asset in dbAssets)
                {
                    var dto = request.Assests.FirstOrDefault(a => a.Assetsid == asset.AssetsId);
                    if (dto != null)
                    {
                        // Convert string to boolean safely
                        bool verifiedStatus = dto.verify?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

                        asset.Verified = verifiedStatus;
                        asset.UpdatedDate = DateTime.Now;
                        asset.UpdatedBy = updatedBy;
                    }
                }

                // Save changes
                await CommonDBContext.SaveChangesAsync();

                // Prepare response
                var result = dbAssets.Select(a => new
                {
                    a.Id,
                    a.EmpId,
                    a.AssetsId,
                    a.Verified,
                    a.UpdatedDate,
                    UpdatedBy = updatedBy
                });

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Assets updated successfully.",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddEmpAssetReturn: {ex.Message}");
                var errorDetails = ex.InnerException?.Message ?? ex.Message;

                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {errorDetails}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetEmpAssetReturn")]
        public async Task<VivifyResponse<object>> GetEmpAssetReturn([FromQuery] int EmpId)
        {
            try
            {
                // Join Employee_Assets with AssetType to get asset name
                var assetEntries = await (from asset in CommonDBContext.Employee_Asset
                                          join assetType in CommonDBContext.AssetTypes
                                          on asset.AssetsId equals assetType.Id into assetGroup
                                          from assetType in assetGroup.DefaultIfEmpty()
                                          where asset.EmpId == EmpId && asset.IsActive
                                          select new
                                          {
                                              asset.Id,
                                              asset.EmpId,
                                              asset.AssetsId,
                                              AssetName = assetType.Type ?? "Unknown",
                                              asset.Verified,
                                              asset.UpdatedDate,
                                              asset.UpdatedBy
                                          }).ToListAsync();

                if (assetEntries == null || !assetEntries.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No employee asset records found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee asset verification details retrieved successfully.",
                    Result = assetEntries
                };
            }
            catch (Exception ex)
            {
                var errorDetails = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {errorDetails}",
                    Result = null
                };
            }
        }




        [HttpGet]
        [ActionName("EditEmpAssetReturn")]
        public async Task<VivifyResponse<object>> EditEmpAssetReturn(
    [FromQuery] int EmpId,
    [FromQuery] int AssetsId)
        {
            try
            {
                var assetEntry = await CommonDBContext.Employee_Asset
                    .FirstOrDefaultAsync(e =>
                        e.EmpId == EmpId &&
                        e.AssetsId == AssetsId &&
                        e.IsActive);

                if (assetEntry == null)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee asset record not found.",
                        Result = null
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Employee asset verification details retrieved successfully.",
                    Result = new
                    {
                        assetEntry.Id,
                        assetEntry.EmpId,
                        assetEntry.AssetsId,
                        assetEntry.Verified,
                        assetEntry.UpdatedDate,
                        assetEntry.UpdatedBy
                    }
                };
            }
            catch (Exception ex)
            {
                var errorDetails = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {errorDetails}",
                    Result = null
                };
            }
        }
      
   
        [HttpGet]
        [ActionName("GetEmployeeAssets")]
        public VivifyResponse<object> GetEmployeeAssets(
      [FromQuery] int? empNo,
      [FromQuery] string? branchId,
      [FromQuery] int? verified)
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                // Base query with joins to EmployeeInfos and MBranches
                var rawAssetQuery = from asset in CommonDBContext.Employee_Asset
                                    join emp in CommonDBContext.EmployeeInfos on asset.EmpId equals emp.EmpNo
                                    join branch in CommonDBContext.MBranches on emp.BranchCode equals branch.BranchCode
                                    where asset.CompanyId == companyId && asset.IsActive
                                    select new
                                    {
                                        asset.EmpId,
                                        emp.BranchCode,
                                        BranchDesc = branch.BranchDesc,
                                        asset.Verified,
                                        emp.FirstName
                                    };

                if (!string.IsNullOrEmpty(branchId))
                {
                    rawAssetQuery = rawAssetQuery.Where(a => a.BranchCode == branchId);
                }

                if (empNo.HasValue)
                {
                    rawAssetQuery = rawAssetQuery.Where(a => a.EmpId == empNo.Value);
                }

                var assetList = rawAssetQuery.ToList();
                List<dynamic> responseData;

                if (verified == 2)
                {
                    var mixedStatusEmployees = assetList
                        .GroupBy(a => a.EmpId)
                        .Where(g => g.Any(x => x.Verified) && g.Any(x => !x.Verified))
                        .Select(g => g.Key)
                        .ToList();

                    responseData = assetList
                        .Where(a => mixedStatusEmployees.Contains(a.EmpId))
                        .Select(a => new
                        {
                            EmployeeDetail = $"{a.EmpId}-{a.FirstName ?? "Unknown"}",
                            BranchDesc = a.BranchDesc ?? "Unknown",
                            Verified = false
                        })
                        .Distinct()
                        .ToList<dynamic>();
                }
                else if (verified == 0)
                {
                    var onlyFalseEmployees = assetList
                        .GroupBy(a => a.EmpId)
                        .Where(g => g.All(x => !x.Verified))
                        .Select(g => g.Key)
                        .ToList();

                    responseData = assetList
                        .Where(a => onlyFalseEmployees.Contains(a.EmpId))
                        .Select(a => new
                        {
                            EmployeeDetail = $"{a.EmpId}-{a.FirstName ?? "Unknown"}",
                            BranchDesc = a.BranchDesc ?? "Unknown",
                            Verified = false
                        })
                        .Distinct()
                        .ToList<dynamic>();
                }
                else if (verified == 1)
                {
                    var onlyTrueEmployees = assetList
                        .GroupBy(a => a.EmpId)
                        .Where(g => g.All(x => x.Verified))
                        .Select(g => g.Key)
                        .ToList();

                    responseData = assetList
                        .Where(a => onlyTrueEmployees.Contains(a.EmpId))
                        .Select(a => new
                        {
                            EmployeeDetail = $"{a.EmpId}-{a.FirstName ?? "Unknown"}",
                            BranchDesc = a.BranchDesc ?? "Unknown",
                            Verified = true
                        })
                        .Distinct()
                        .ToList<dynamic>();
                }
                else
                {
                    responseData = assetList
                        .GroupBy(a => a.EmpId)
                        .Select(g => new
                        {
                            EmployeeId = g.Key,
                            FirstName = g.First().FirstName,
                            BranchDesc = g.First().BranchDesc,
                            AllVerified = g.All(x => x.Verified)
                        })
                        .Select(a => new
                        {
                            EmployeeDetail = $"{a.EmployeeId}-{a.FirstName ?? "Unknown"}",
                            BranchDesc = a.BranchDesc ?? "Unknown",
                            Verified = a.AllVerified
                        })
                        .ToList<dynamic>();
                }

                if (!responseData.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No records found.",
                        Result = new { message = "No asset assignment records found." }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = responseData
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { message = "An error occurred while fetching asset records." }
                };
            }
        }
        [HttpGet]
        [ActionName("GetVerifiedAssets")]
        public VivifyResponse<object> GetVerifiedAssets([FromQuery] int empId)
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims
                    .FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                var assets = (from ea in CommonDBContext.Employee_Asset
                              join a in CommonDBContext.AssetTypes on ea.AssetsId equals a.Id
                              join emp in CommonDBContext.EmployeeInfos
                                  on ea.EmpId equals emp.EmpNo into empGroup
                              from emp in empGroup.DefaultIfEmpty()
                              where ea.EmpId == empId &&
                                    ea.CompanyId == companyId &&
                                    ea.IsActive
                              select new
                              {
                                  employeeDetail = emp != null ? $"{emp.EmpNo}-{emp.FirstName}" : $"Unknown-{empId}",
                                  ea.AssetsId,
                                  AssetName = a.Type,
                                  ea.Verified,
                                  ea.ReturningStatus,
                                  ea.CreatedDate,
                                  ea.DocPath
                              })
                             .ToList();

                if (!assets.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No records found.",
                        Result = new { message = $"No assets found for employee ID: {empId}" }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = assets
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { message = "An error occurred while fetching asset records." }
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmpInductionStatus")]
        public async Task<VivifyResponse<object>> AddEmpInductionStatus([FromQuery] int inductionId)
        {
            try
            {
                // 1. Get EmpID and CompanyID from JWT token
                var empNoClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo");
                var companyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "CompanyID");

                if (empNoClaim == null || !int.TryParse(empNoClaim.Value, out int empId) ||
                    companyIdClaim == null || !int.TryParse(companyIdClaim.Value, out int companyId))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 401,
                        StatusDesc = "Unauthorized: Invalid or missing claims in token.",
                        Result = null
                    };
                }

                // 2. Validate inductionId
                if (inductionId <= 0)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid InductionId. Must be greater than 0.",
                        Result = null
                    };
                }

                // 3. Check if induction exists (company-specific check)
                var inductionExists = await CommonDBContext.InductionInfos
                    .AnyAsync(i => i.Id == inductionId && i.CompanyId== companyId);

                if (!inductionExists)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Induction not found for your company.",
                        Result = null
                    };
                }

                // 4. ALWAYS CREATE NEW RECORD (ignore existing entries)
                var newRecord = new InductionEmpStatus
                {
                    InductionId = inductionId,
                    EmpId = empId,
                    CompanyId = companyId,
                    Status = 1, // Completed
                    WatchedDate = DateTime.Now
                   
                };

                await CommonDBContext.InductionEmpStatuses.AddAsync(newRecord);
                await CommonDBContext.SaveChangesAsync();

                // 5. Return success
                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "New induction completion record created.",
                    Result = new
                    {
                        newRecord.Id,
                        newRecord.InductionId,
                        newRecord.EmpId,
                        newRecord.CompanyId,
                        newRecord.Status,
                        newRecord.WatchedDate
                       
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] AddEmpInductionStatus: {ex}");
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetAllInductions")]
        public VivifyResponse<object> GetAllInductions(
     [FromQuery] long? empID = null,
     [FromQuery] DateOnly? startDate = null,
     [FromQuery] DateOnly? endDate = null,
     [FromQuery] string? branchCode = null,
     [FromQuery] int? inductionId = null) // InductionInfo.Id
        {
            try
            {
                int CompanyID = Convert.ToInt32(HttpContext.User.Claims
                    .FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                Console.WriteLine($"Input Parameters: empID={empID}, startDate={startDate}, endDate={endDate}, branchCode={branchCode}, inductionId={inductionId}, CompanyID={CompanyID}");

                // Start building the query
                var inductionsQuery = from ind in CommonDBContext.InductionEmpStatuses
                                      join emp in CommonDBContext.EmployeeInfos on ind.EmpId equals emp.EmpNo
                                      join branch in CommonDBContext.MBranches on emp.BranchCode equals branch.BranchCode
                                      join indType in CommonDBContext.InductionInfos on ind.InductionId equals indType.Id into indGroup
                                      from indType in indGroup.DefaultIfEmpty()
                                      where ind.CompanyId == CompanyID
                                      select new
                                      {
                                          ind.EmpId,
                                          EmpNo = emp.EmpNo,
                                          EmployeeName = emp.FirstName + " " + emp.LastName,
                                          BranchCode = emp.BranchCode,
                                          BranchID = branch.BranchCode,
                                          BranchName = branch.BranchDesc,
                                          InductionId = ind.InductionId, // Ensure this is the correct column
                                          InductionName = indType != null ? indType.InductionName : "N/A", // Get InductionName from InductionInfo
                                          ind.WatchedDate,
                                          ind.Status
                                      };

                // Apply filters
                if (empID.HasValue)
                {
                    Console.WriteLine($"Filtering by EmpID = {empID.Value}");
                    inductionsQuery = inductionsQuery.Where(x => x.EmpId == empID.Value);
                }

                if (startDate.HasValue && endDate.HasValue)
                {
                    var startDateTime = startDate.Value.ToDateTime(TimeOnly.MinValue);
                    var endDateTime = endDate.Value.ToDateTime(TimeOnly.MaxValue);
                    Console.WriteLine($"Filtering by WatchedDate between {startDateTime} and {endDateTime}");
                    inductionsQuery = inductionsQuery.Where(x => x.WatchedDate >= startDateTime && x.WatchedDate <= endDateTime);
                }

                if (!string.IsNullOrEmpty(branchCode))
                {
                    Console.WriteLine($"Filtering by BranchCode = {branchCode}");
                    inductionsQuery = inductionsQuery.Where(x => x.BranchCode == branchCode);
                }

                if (inductionId.HasValue)
                {
                    Console.WriteLine($"Filtering by InductionId = {inductionId.Value}");
                    // Ensure this is filtering by Induction_Id in InductionEmpStatus
                    inductionsQuery = inductionsQuery.Where(x => x.InductionId == inductionId.Value);
                }

                // Execute the query and get the results
                var inductions = inductionsQuery.ToList();

                // Debugging: check how many records were found
                Console.WriteLine($"Found {inductions.Count} inductions for InductionId = {inductionId}");

                if (!inductions.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No records found.",
                        Result = new { message = "No records found." }
                    };
                }

                // Return the response with filtered data
                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Loaded Successfully",
                    Result = inductions.Select(x => new
                    {
                        x.EmpId,
                        x.EmployeeName,
                        x.BranchCode,
                        x.BranchName,
                        x.InductionId,
                        x.InductionName,
                        WatchedDate = x.WatchedDate,
                        x.Status
                    })
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { message = "An error occurred." }
                };
            }
        }
        [HttpPost]
        [ActionName("InsertMonthlyPayComponents")]
        public async Task<VivifyResponse<object>> InsertMonthlyPayComponents([FromForm] InsertMonthlyPayComponentsRequest request)
        {
            using var transaction = await CommonDBContext.Database.BeginTransactionAsync();
            try
            {
                // Validate MonthYear format
                if (string.IsNullOrWhiteSpace(request.MonthYear) ||
                    request.MonthYear.Length != 7 ||
                    request.MonthYear[4] != '-')
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid MonthYear format. Expected format: yyyy-MM.",
                        Result = null
                    };
                }

                string monthYear = request.MonthYear;

                // Check if this month is locked or confirmed
                var controlRecord = await CommonDBContext.EmployeeMonthlyPayrollSummaryControls
                    .FirstOrDefaultAsync(c => c.PayMonthYear == monthYear);
                if (controlRecord != null && controlRecord.PayrollStatusTypeId >= 2)
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = $"This month '{monthYear}' has already been confirmed.",
                        Result = null
                    };
                }

                // Get current user EmpID from token claims
                var currentUserClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value;
                if (string.IsNullOrEmpty(currentUserClaim))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "User information not found in token.",
                        Result = null
                    };
                }

                int currentUserEmpID = Convert.ToInt32(currentUserClaim);
                int year = int.Parse(monthYear.Substring(0, 4));
                int month = int.Parse(monthYear.Substring(5, 2));
                var payStartDate = new DateTime(year, month, 1);
                var payEndDate = payStartDate.AddMonths(1).AddDays(-1);

                // Fetch all active employees
                var allEmployees = await CommonDBContext.EmployeeInfos
                    .Where(emp => emp.IsActive)
                    .Select(emp => emp.EmpNo)
                    .ToListAsync();

                if (!allEmployees.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No active employees found.",
                        Result = null
                    };
                }

                // Get employees who already have payroll for this MonthYear
                var existingSummaries = await CommonDBContext.EmployeeMonthlyPayrollSummaries
                    .Where(s => allEmployees.Contains(s.EmpId) && s.PayMonthYear == monthYear)
                    .Select(s => s.EmpId)
                    .ToListAsync();

                // Filter out existing ones — only process NEW employees
                var newEmployeesOnly = allEmployees.Where(empId => !existingSummaries.Contains(empId)).ToList();

                if (!newEmployeesOnly.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = $"All employees already have payroll summaries for {monthYear}. No new entries to process.",
                        Result = null
                    };
                }

                // Fetch all active benefits for new employees
                var benefits = await (from benefit in CommonDBContext.EmployeeBenefits
                                      where newEmployeesOnly.Contains(benefit.EmpId) &&
                                            benefit.IsActive == true
                                      select benefit).ToListAsync();

                // Fetch all active deductions for new employees
                var deductions = await CommonDBContext.EmployeeDeductions
                    .Where(d => newEmployeesOnly.Contains(d.EmpId) &&
                                d.Active == 1 &&
                                d.MonthlyRepayment_Amt.HasValue &&
                                d.MonthlyRepayment_Amt > 0)
                    .Select(d => new
                    {
                        d.DeductionID,
                        d.EmpId,
                        d.PayComponentID,
                        Amount = -d.MonthlyRepayment_Amt.Value,
                        d.Repayment_StartMonthYear,
                        d.Repayment_EndMonthYear,
                        d.RepaymentMonths,
                        d.MonthsPaid,
                        d.RepaymentComplete
                    })
                    .ToListAsync()
                    .ContinueWith(task =>
                    {
                        return task.Result
                            .Where(d =>
                            {
                                try
                                {
                                    // Skip date check if RepaymentMonths == 0
                                    if (d.RepaymentMonths == 0)
                                        return true;

                                    var startParts = d.Repayment_StartMonthYear.Split('/');
                                    var endParts = d.Repayment_EndMonthYear.Split('/');
                                    var repaymentStart = new DateTime(int.Parse(startParts[2]), int.Parse(startParts[1]), 1);
                                    var repaymentEnd = new DateTime(int.Parse(endParts[2]), int.Parse(endParts[1]), 1)
                                        .AddMonths(1).AddDays(-1);

                                    return payStartDate >= repaymentStart && payStartDate <= repaymentEnd;
                                }
                                catch
                                {
                                    return false;
                                }
                            }).ToList();
                    });

                var toInsert = new List<EmployeeMonthlyPayComponent>();
                var toUpdate = new List<EmployeeMonthlyPayComponent>();

                foreach (var empId in newEmployeesOnly)
                {
                    var empBenefits = benefits.Where(b => b.EmpId == empId).ToList();
                    var empDeductions = deductions.Where(d => d.EmpId == empId).ToList();

                    if (!empBenefits.Any() && !empDeductions.Any())
                        continue;

                    // Delete existing records (shouldn't be any, but just in case)
                    var existingRecordsToDelete = await CommonDBContext.Employee_MonthlyPayComponents
                        .Where(r => r.EmpId == empId && r.PayMonthYear == monthYear)
                        .ToListAsync();

                    if (existingRecordsToDelete.Any())
                    {
                        CommonDBContext.Employee_MonthlyPayComponents.RemoveRange(existingRecordsToDelete);
                        await CommonDBContext.SaveChangesAsync();
                    }

                    var existingRecords = new Dictionary<int, EmployeeMonthlyPayComponent>();

                    // Process Benefits
                    foreach (var benefit in empBenefits)
                    {
                        ProcessPayComponent(
                            benefit.EmpId,
                            benefit.PayComponentId,
                            benefit.Amount,
                            monthYear,
                            payStartDate,
                            payEndDate,
                            currentUserEmpID,
                            existingRecords,
                            toInsert,
                            toUpdate);
                    }

                    // Process Deductions
                    foreach (var deduction in empDeductions)
                    {
                        // Skip one-time deductions that were already paid
                        if (deduction.RepaymentMonths == 1 && deduction.MonthsPaid >= 1)
                        {
                            continue;
                        }

                        ProcessPayComponent(
                            deduction.EmpId,
                            deduction.PayComponentID,
                            deduction.Amount,
                            monthYear,
                            payStartDate,
                            payEndDate,
                            currentUserEmpID,
                            existingRecords,
                            toInsert,
                            toUpdate);
                    }

                    // Update Payroll Summary
                    decimal totalEarnings = empBenefits.Sum(b => b.Amount);
                    decimal totalDeductions = Math.Abs(empDeductions.Sum(d => d.Amount));

                    var payrollSummary = new EmployeeMonthlyPayrollSummary
                    {
                        EmpId = empId,
                        PayMonthYear = monthYear,
                        PayStartDate = payStartDate,
                        PayEndDate = payEndDate,
                        TotalDaysPayCycle = (payEndDate - payStartDate).Days + 1,
                        TotalEarnings = totalEarnings,
                        TotalOtherDeductions = totalDeductions,
                        NetPay = totalEarnings - totalDeductions,
                        PayrollStatusTypeId = request.PayrollStatusTypeId ?? 1,
                        LastUpdatedEmpId = currentUserEmpID,
                        LastUpdatedDate = DateOnly.FromDateTime(DateTime.Now)
                    };

                    await CommonDBContext.EmployeeMonthlyPayrollSummaries.AddAsync(payrollSummary);

                    // Update Deduction Records
                    foreach (var deduction in empDeductions)
                    {
                        var dbDeduction = await CommonDBContext.EmployeeDeductions
                            .FirstOrDefaultAsync(d => d.DeductionID == deduction.DeductionID);

                        if (dbDeduction != null)
                        {
                            dbDeduction.MonthsPaid = (dbDeduction.MonthsPaid ?? 0) + 1;

                            // Only update RepaymentComplete & Active if RepaymentMonths > 0
                            if (dbDeduction.RepaymentMonths > 0)
                            {
                                if (dbDeduction.RepaymentMonths > 1 &&
                                    dbDeduction.MonthsPaid >= dbDeduction.RepaymentMonths)
                                {
                                    dbDeduction.RepaymentComplete = 1;
                                    dbDeduction.Active = 0; // Deactivate after full repayment
                                }
                            }
                            else
                            {
                                // For indefinite deductions, never complete and always active
                                dbDeduction.RepaymentComplete = 0;
                                dbDeduction.Active = 1;
                            }

                            dbDeduction.LastUpdatedByEmpId = currentUserEmpID;
                            dbDeduction.LastUpdatedDate = DateTime.Now;
                            CommonDBContext.EmployeeDeductions.Update(dbDeduction);
                        }
                    }
                }

                if (toUpdate.Count > 0)
                {
                    CommonDBContext.Employee_MonthlyPayComponents.UpdateRange(toUpdate);
                }

                if (toInsert.Count > 0)
                {
                    await CommonDBContext.Employee_MonthlyPayComponents.AddRangeAsync(toInsert);
                }

                await CommonDBContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = $"Employees processed successfully for {monthYear}.",
                    Result = new
                    {
                        TotalNewEmployeesProcessed = newEmployeesOnly.Count,
                        TotalInserted = toInsert.Count,
                        TotalUpdated = toUpdate.Count
                    }
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }

        private void ProcessPayComponent(
            int empId,
            int payComponentId,
            decimal amount,
            string monthYear,
            DateTime payStartDate,
            DateTime payEndDate,
            int currentUserEmpID,
            Dictionary<int, EmployeeMonthlyPayComponent> existingRecords,
            List<EmployeeMonthlyPayComponent> toInsert,
            List<EmployeeMonthlyPayComponent> toUpdate)
        {
            if (existingRecords.TryGetValue(payComponentId, out var existing))
            {
                existing.EmpId = empId;
                existing.PayMonthYear = monthYear;
                existing.PayComponentId = payComponentId;
                existing.PayStartDate = payStartDate;
                existing.PayEndDate = payEndDate;
                existing.Amount = amount;
                existing.Note = null;
                existing.EntryType = 1;
                existing.LastUpdatedByEmpId = currentUserEmpID;
                existing.LastUpdatedDate = DateTime.Now;
                toUpdate.Add(existing);
            }
            else
            {
                toInsert.Add(new EmployeeMonthlyPayComponent
                {
                    EmpId = empId,
                    PayMonthYear = monthYear,
                    PayComponentId = payComponentId,
                    PayStartDate = payStartDate,
                    PayEndDate = payEndDate,
                    Amount = amount,
                    Note = null,
                    EntryType = 1,
                    LastUpdatedByEmpId = currentUserEmpID,
                    LastUpdatedDate = DateTime.Now
                });
            }
        }

        [HttpGet]
        [ActionName("GetEmployeeBranchAndCompanyDetails")]
        public VivifyResponse<object> GetEmployeeBranchAndCompanyDetails([FromQuery] int empNo)
        {
            try
            {
                var companyIdClaim = HttpContext.User.Claims
                    .FirstOrDefault(x => x.Type == "CompanyID")?.Value;

                if (string.IsNullOrEmpty(companyIdClaim))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Unauthorized or missing CompanyID in token.",
                        Result = new { message = "Missing CompanyID in claims." }
                    };
                }

                int companyId = Convert.ToInt32(companyIdClaim);

                Console.WriteLine($"Fetching for EmpNo={empNo}, CompanyId={companyId}");

                var result = (from emp in CommonDBContext.EmployeeInfos
                              join comp in CommonDBContext.CompanyInfo on emp.CompanyId equals comp.CompanyId
                              join branch in CommonDBContext.MBranches
                                on new { emp.BranchCode, emp.CompanyId } equals new { branch.BranchCode, branch.CompanyId }
                              where emp.EmpNo == empNo && emp.IsActive
                              select new
                              {
                                  EmpId = emp.EmpNo,
                                  EmpName = emp.FirstName,
                                  CompanyId = comp.CompanyId,
                                  CompanyName = comp.CompanyName,
                                  BranchId = branch.BranchCode,
                                  BranchName = branch.BranchDesc
                              }).FirstOrDefault();

                if (result == null)
                {
                    Console.WriteLine($"No matching record found for EmpNo={empNo}.");
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "Not Found",
                        Result = new { message = $"No active employee found with EmpNo: {empNo}" }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Success",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred: {ex.Message}");
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = "Error",
                    Result = new
                    {
                        message = "An error occurred while fetching data.",
                        error = ex.Message
                    }
                };
            }
        }
        [HttpGet]
        [ActionName("GetHrmsAdminCount")]
        public VivifyResponse<object> GetHrmsAdminCount()
        {
            try
            {
                int activeEmployeeCount = CommonDBContext.EmployeeInfos
                    .Count(e => e.IsActive == true);

                int totalEmployeeCount = CommonDBContext.EmployeeInfos.Count();

                int inactiveEmployeeCount = totalEmployeeCount - activeEmployeeCount;

               
                int pendingAdvanceCount = HRMSDBContext.Advances
                    .Count(a => a.Status == "0");

                
                int pendingLeaveCount = HRMSDBContext.LeaveRequests
                    .Count(l => l.Status == "0");

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Successfully retrieved counts",
                    Result = new
                    {
                        ActiveEmployeeCount = activeEmployeeCount,
                        TotalEmployeeCount = totalEmployeeCount,
                        InactiveEmployeeCount = inactiveEmployeeCount,
                        PendingAdvanceRequestCount = pendingAdvanceCount,
                        PendingLeaveRequestCount = pendingLeaveCount
                    }
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
        [ActionName("GetAllEmployeeMonthlyPayroll")]
        public VivifyResponse<List<EmployeePayrollResponse>> GetAllEmployeeMonthlyPayroll(
     [FromQuery] string MonthYear)
        {
            try
            {
                var query = from emp in CommonDBContext.EmployeeInfos
                            where emp.IsActive == true// 👈 Filter only active employees
                            join branch in CommonDBContext.MBranches
                            on emp.BranchCode equals branch.BranchCode
                            select new
                            {
                                emp.EmpNo,
                                emp.FirstName,
                                branch.BranchDesc
                            };

                var employees = query.ToList();

                if (!employees.Any())
                {
                    return new VivifyResponse<List<EmployeePayrollResponse>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No employees found.",
                        Result = null
                    };
                }

                IQueryable<EmployeeMonthlyPayrollSummary> payrollQuery =
                    CommonDBContext.EmployeeMonthlyPayrollSummaries;

                if (!string.IsNullOrWhiteSpace(MonthYear))
                {
                    payrollQuery = payrollQuery.Where(p => p.PayMonthYear == MonthYear);
                }

                var payrollData = payrollQuery.ToList();

                var result = (from emp in employees
                              join payroll in payrollData
                              on emp.EmpNo equals payroll.EmpId into gj
                              from subPayroll in gj.DefaultIfEmpty()
                              select new EmployeePayrollResponse
                              {
                                  EmpNo = emp.EmpNo,
                                  EmpName = emp.FirstName,
                                  BranchName = emp.BranchDesc,

                                  // Payroll Summary Data
                                  PayMonthYear = subPayroll?.PayMonthYear,
                                  PayStartDate = subPayroll?.PayStartDate,
                                  PayEndDate = subPayroll?.PayEndDate,
                                  TotalDaysPayCycle = subPayroll?.TotalDaysPayCycle,
                                  TotalDaysUnpaid = subPayroll?.TotalDaysUnpaid,
                                  NetPayDays = subPayroll?.NetPayDays,
                                  BankAccount = subPayroll?.BankAccount,
                                  BankId = subPayroll?.BankId,
                                  PayrollNote = subPayroll?.PayrollNote,
                                  PayrollStatusTypeId = subPayroll?.PayrollStatusTypeId,
                                  TotalEarnings = subPayroll?.TotalEarnings,
                                  LopDed = subPayroll?.LopDed,
                                  TotalOtherDeductions = subPayroll?.TotalOtherDeductions,
                                  NetPay = subPayroll?.NetPay,
                                  Eospayout = subPayroll?.Eospayout,
                                  UnusedVacDays = subPayroll?.UnusedVacDays,
                                  UnusedDaysAmt = subPayroll?.UnusedDaysAmt,
                                  LastUpdatedEmpId = subPayroll?.LastUpdatedEmpId,
                                  LastUpdatedDate = subPayroll?.LastUpdatedDate
                              }).ToList();

                string statusDesc = string.IsNullOrWhiteSpace(MonthYear)
                    ? "All employees retrieved successfully with optional payroll data."
                    : $"Employees and payroll data for {MonthYear} retrieved successfully.";

                return new VivifyResponse<List<EmployeePayrollResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = statusDesc,
                    Result = result
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<EmployeePayrollResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error retrieving employee payroll data: {ex.Message}. Inner Exception: {innerEx}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetAllProcessedPayrollData")]
        public VivifyResponse<List<EmployeeMonthlyPayrollSummaryControl>> GetAllProcessedPayrollData(
    [FromQuery] string payMonthYear = null!)
        {
            try
            {
                // Validate input format (yyyy-MM)
                if (!string.IsNullOrWhiteSpace(payMonthYear) &&
                    !Regex.IsMatch(payMonthYear, @"^\d{4}-\d{2}$"))
                {
                    return new VivifyResponse<List<EmployeeMonthlyPayrollSummaryControl>>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid MonthYear format. Expected format: 'yyyy-MM'",
                        Result = null
                    };
                }

                // Start query
                IQueryable<EmployeeMonthlyPayrollSummaryControl> query =
                    CommonDBContext.EmployeeMonthlyPayrollSummaryControls;

                // Apply filter if provided
                if (!string.IsNullOrWhiteSpace(payMonthYear))
                {
                    query = query.Where(x => x.PayMonthYear == payMonthYear);
                }

                var result = query.ToList();

                // Handle empty result
                if (!result.Any())
                {
                    return new VivifyResponse<List<EmployeeMonthlyPayrollSummaryControl>>
                    {
                        StatusCode = 404,
                        StatusDesc = string.IsNullOrWhiteSpace(payMonthYear)
                            ? "No payroll control records found."
                            : $"No records found for month/year: {payMonthYear}",
                        Result = null
                    };
                }

                // Success response
                return new VivifyResponse<List<EmployeeMonthlyPayrollSummaryControl>>
                {
                    StatusCode = 200,
                    StatusDesc = string.IsNullOrWhiteSpace(payMonthYear)
                        ? "All payroll control records retrieved successfully."
                        : $"Payroll control records for {payMonthYear} retrieved successfully.",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                // Log exception here (use ILogger in production)
                Console.Error.WriteLine($"Error retrieving payroll control data: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }

                return new VivifyResponse<List<EmployeeMonthlyPayrollSummaryControl>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error retrieving payroll control data: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpPost]
        [ActionName("ProcessAndFinalizePayrollSummary")]
        public async Task<VivifyResponse<object>> ProcessAndFinalizePayrollSummary(
      [FromForm] ProcessPayrollSummaryRequest request)
        {
            using var transaction = await CommonDBContext.Database.BeginTransactionAsync();
            try
            {
                // Validate MonthYear format
                if (string.IsNullOrWhiteSpace(request.PayMonthYear) ||
                    request.PayMonthYear.Length != 7 ||
                    request.PayMonthYear[4] != '-')
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid MonthYear format. Expected format: yyyy-MM.",
                        Result = null
                    };
                }

                string payMonthYear = request.PayMonthYear;

                // Get current user EmpID from token
                var currentUserClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value;
                if (string.IsNullOrEmpty(currentUserClaim))
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = "User EmpNo not found in token.",
                        Result = null
                    };
                }

                int currentUserEmpID = Convert.ToInt32(currentUserClaim);

                // Fetch all payroll summaries for the given month/year where status is 1 (unlocked)
                var payrollSummaries = await CommonDBContext.EmployeeMonthlyPayrollSummaries
                    .Where(p => p.PayMonthYear == payMonthYear && p.PayrollStatusTypeId == 1)
                    .ToListAsync();

                if (!payrollSummaries.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 400,
                        StatusDesc = $"Aleready Payroll Reviewed for month/year: {payMonthYear}.",
                        Result = null
                    };
                }

                // Update each summary to final status (status = 2)
                var now = DateOnly.FromDateTime(DateTime.Now);
                foreach (var summary in payrollSummaries)
                {
                    summary.PayrollStatusTypeId = 2; // Mark as finalized
                    summary.LastUpdatedEmpId = currentUserEmpID;
                    summary.LastUpdatedDate = now;
                }

                CommonDBContext.EmployeeMonthlyPayrollSummaries.UpdateRange(payrollSummaries);
                await CommonDBContext.SaveChangesAsync();

                // Prepare control record
                var divisionClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "DivisionId")?.Value;
                var locationClaim = HttpContext.User.Claims.FirstOrDefault(c => c.Type == "LocationId")?.Value;

                int? divisionId = string.IsNullOrEmpty(divisionClaim) ? (int?)null : Convert.ToInt32(divisionClaim);
                int? locationId = string.IsNullOrEmpty(locationClaim) ? (int?)null : Convert.ToInt32(locationClaim);

                var controlRecord = new EmployeeMonthlyPayrollSummaryControl
                {
                    DivisionId = divisionId,
                    LocationId = locationId,
                    PayMonthYear = payMonthYear,
                    PayrollStatusTypeId = 2,
                    TotalEmployeesCount = payrollSummaries.Count,
                    TotalPostedAmount = payrollSummaries.Sum(p => p.NetPay ?? 0),
                    TotalEoscount = payrollSummaries.Sum(p => p.Eospayout ?? 0),
                    PayrollRunDate = DateTime.Now,
                    LastUpdatedEmpId = currentUserEmpID,
                    LastUpdatedDate = DateTime.Now
                };

                // Check for existing control record
                var existingControlRecord = await CommonDBContext.EmployeeMonthlyPayrollSummaryControls
                    .FirstOrDefaultAsync(c => c.PayMonthYear == payMonthYear);

                if (existingControlRecord != null)
                {
                    // Update existing control record
                    existingControlRecord.PayrollStatusTypeId = controlRecord.PayrollStatusTypeId;
                    existingControlRecord.TotalEmployeesCount = controlRecord.TotalEmployeesCount;
                    existingControlRecord.TotalPostedAmount = controlRecord.TotalPostedAmount;
                    existingControlRecord.TotalEoscount = controlRecord.TotalEoscount;
                    existingControlRecord.PayrollRunDate = controlRecord.PayrollRunDate;
                    existingControlRecord.LastUpdatedEmpId = currentUserEmpID;
                    existingControlRecord.LastUpdatedDate = DateTime.Now;

                    CommonDBContext.EmployeeMonthlyPayrollSummaryControls.Update(existingControlRecord);
                }
                else
                {
                    // Insert new control record
                    await CommonDBContext.EmployeeMonthlyPayrollSummaryControls.AddAsync(controlRecord);
                }

                await CommonDBContext.SaveChangesAsync();

                // Commit transaction
                await transaction.CommitAsync();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = $"Payroll finalized for {payMonthYear}.",
                    Result = new
                    {
                        TotalEmployeesProcessed = payrollSummaries.Count,
                        TotalSalaryPosted = payrollSummaries.Sum(p => p.NetPay ?? 0),
                        UpdatedStatus = 2
                    }
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error processing payroll: {ex.Message}. Inner Exception: {ex.InnerException?.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetExpenseTypes")]
        public VivifyResponse<List<ExpenseTypeResponse>> GetExpenseTypes()
        {
            try
            {
                var expenseTypes = HRMSDBContext.ExpenseTypes
                    .Select(x => new ExpenseTypeResponse
                    {
                        ExpenseId = x.ExpenseId,
                        ExpenseName = x.ExpenseName
                    })
                    .ToList();

                return new VivifyResponse<List<ExpenseTypeResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Expense types fetched successfully.",
                    Result = expenseTypes
                };
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException?.Message ?? ex.Message;
                return new VivifyResponse<List<ExpenseTypeResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}. Inner Exception: {innerEx}",
                    Result = new List<ExpenseTypeResponse>()
                };
            }
        }
        [HttpPost]
        [ActionName("AddEmployeeExpenses")]
        [Authorize] 
        public async Task<VivifyResponse<string>> AddEmployeeExpenses([FromForm] EmployeeExpensesRequest request)
        {
            try
            {
                long empIdFromToken = 0;
                var empClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value;

                if (!string.IsNullOrEmpty(empClaim))
                {
                    long.TryParse(empClaim, out empIdFromToken);
                }


                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == request.EmpId);

                if (employee == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = null
                    };
                }

                decimal totalAmount = 0;
                foreach (var expense in request.Expenses)
                {
                    string savedFilePath = null;

                    // Attachment is optional
                    if (expense.Attachment != null)
                    {
                        savedFilePath = await SaveAttachmentFileAsync(expense.Attachment);
                    }

                    var reimbursement = new EmployeeExpense
                    {
                        EmpId = request.EmpId,
                        ExpenseTypeId = expense.ExpenseTypeId,
                        Date = expense.Date,
                        Remarks = expense.Remarks,
                        Amount = expense.Amount,
                        CrtBy = empIdFromToken.ToString(),
                        CrtDate = DateTime.Now,
                        Status = false,
                        CompanyId = employee.CompanyId,
                        BranchId = employee.BranchCode,
                        Attachment = savedFilePath
                    };

                    HRMSDBContext.EmployeeExpenses.Add(reimbursement);
                    totalAmount += expense.Amount;
                }


                await HRMSDBContext.SaveChangesAsync();

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = $"Expenses added successfully.",
                    Result = totalAmount.ToString("F2")
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = null
                };
            }
        }



        [HttpGet]
[ActionName("GetPendingExpensesReport")]
[Authorize]
public VivifyResponse<object> GetPendingExpensesReport(
    [FromQuery] long? empId = null,
    [FromQuery] DateOnly? fromDate = null,
    [FromQuery] DateOnly? toDate = null,
    [FromQuery] string? branchId = null,
    [FromQuery] int? expenseTypeId = null)
{
    try
    {
        int companyId = Convert.ToInt32(HttpContext.User.Claims
            .FirstOrDefault(x => x.Type == "CompanyID")?.Value);

        var expensesQuery = HRMSDBContext.EmployeeExpenses
            .Where(e => e.Status == false);

        if (empId.HasValue)
            expensesQuery = expensesQuery.Where(e => e.EmpId == empId.Value);

        if (!string.IsNullOrEmpty(branchId))
            expensesQuery = expensesQuery.Where(e => e.BranchId.Trim() == branchId.Trim());

        if (expenseTypeId.HasValue)
            expensesQuery = expensesQuery.Where(e => e.ExpenseTypeId == expenseTypeId.Value);

        if (fromDate.HasValue && toDate.HasValue)
        {
            var fromDateTime = fromDate.Value.ToDateTime(TimeOnly.MinValue);
            var toDateTime = toDate.Value.ToDateTime(TimeOnly.MaxValue);
            expensesQuery = expensesQuery.Where(e => e.Date >= fromDateTime && e.Date <= toDateTime);
        }
        else if (fromDate.HasValue)
        {
            var fromDateTime = fromDate.Value.ToDateTime(TimeOnly.MinValue);
            expensesQuery = expensesQuery.Where(e => e.Date >= fromDateTime);
        }
        else if (toDate.HasValue)
        {
            var toDateTime = toDate.Value.ToDateTime(TimeOnly.MaxValue);
            expensesQuery = expensesQuery.Where(e => e.Date <= toDateTime);
        }

        var expenses = expensesQuery.ToList();

        if (!expenses.Any())
        {
            return new VivifyResponse<object>
            {
                StatusCode = 404,
                StatusDesc = "No pending expenses found.",
                Result = new { message = "No pending expenses match the criteria." }
            };
        }

        var empIds = expenses.Select(e => e.EmpId).Distinct().ToList();
        var employees = CommonDBContext.EmployeeInfos
            .Where(emp => empIds.Contains(emp.EmpNo)).ToList();

        var branchCodes = expenses.Select(e => e.BranchId.Trim()).Distinct().ToList();
        var branches = CommonDBContext.MBranches
            .Where(b => branchCodes.Contains(b.BranchCode.Trim())).ToList();

        var expenseTypes = HRMSDBContext.ExpenseTypes.ToList();

        var resultList = expenses.Select(e =>
        {
            var emp = employees.FirstOrDefault(x => x.EmpNo == e.EmpId);
            var branch = branches.FirstOrDefault(b => b.BranchCode.Trim() == e.BranchId.Trim());
            var expenseType = expenseTypes.FirstOrDefault(et => et.ExpenseId == e.ExpenseTypeId);

            return new
            {
                e.Id,
                e.EmpId,
                EmployeeName = emp != null ? $"{emp.FirstName} {emp.LastName}" : e.EmpId.ToString(),
                BranchCode = e.BranchId,
                BranchName = branch?.BranchDesc ?? "Unknown",
                ExpenseTypeId = e.ExpenseTypeId,
                ExpenseTypeName = expenseType?.ExpenseName ?? "Unknown",
                Date = e.Date,
                e.Status
            };
        }).ToList();

        return new VivifyResponse<object>
        {
            StatusCode = 200,
            StatusDesc = "Pending expenses fetched successfully.",
            Result = resultList
        };
    }
    catch (Exception ex)
    {
        return new VivifyResponse<object>
        {
            StatusCode = 500,
            StatusDesc = $"Error: {ex.Message}",
            Result = new { message = "An error occurred while processing the request." }
        };
    }
}
        [HttpGet]
        [ActionName("GetExpenseEmpReport")]
        [Authorize]
        public VivifyResponse<List<EmployeeExpenseResponse>> GetExpenseEmpReport(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                // Get CompanyID from JWT claims
                int companyID = Convert.ToInt32(HttpContext.User.Claims
                    .FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                // Get EmpNo from JWT claims
                var empNoClaim = HttpContext.User.Claims
                    .FirstOrDefault(x => x.Type == "EmpNo")?.Value;

                if (!long.TryParse(empNoClaim, out long empNo))
                {
                    return new VivifyResponse<List<EmployeeExpenseResponse>>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing EmpNo claim.",
                        Result = new List<EmployeeExpenseResponse>()
                    };
                }

                var expenseList = (
                    from expense in HRMSDBContext.EmployeeExpenses
                    join expType in HRMSDBContext.ExpenseTypes
                        on expense.ExpenseTypeId equals expType.ExpenseId into expTypeGroup
                    from expType in expTypeGroup.DefaultIfEmpty()

                    where expense.EmpId == empNo
                    && expense.CompanyId == companyID
                    && (!fromDate.HasValue || expense.Date >= fromDate.Value)
                    && (!toDate.HasValue || expense.Date <= toDate.Value)

                    select new EmployeeExpenseResponse
                    {
                        Id = expense.Id,
                        EmpId = expense.EmpId,
                       
                        BranchName = expense.BranchId ?? "Unknown",
                        ExpenseTypeId = expense.ExpenseTypeId,
                        ExpenseTypeName = expType != null ? expType.ExpenseName : "Unknown",
                        Date = expense.Date,
                        Amount = expense.Amount,
                        Remarks = expense.Remarks ?? string.Empty,
                        Status = expense.Status
                    }).ToList();

                if (!expenseList.Any())
                {
                    return new VivifyResponse<List<EmployeeExpenseResponse>>
                    {
                        StatusCode = 404,
                        StatusDesc = "No expenses found for the  given date range.",
                        Result = new List<EmployeeExpenseResponse>()
                    };
                }

                return new VivifyResponse<List<EmployeeExpenseResponse>>
                {
                    StatusCode = 200,
                    StatusDesc = "Expenses loaded successfully.",
                    Result = expenseList
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<List<EmployeeExpenseResponse>>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new List<EmployeeExpenseResponse>()
                };
            }
        }


        [HttpGet]
        [ActionName("GetExpensesActionReport")]
        [Authorize]
        public VivifyResponse<object> GetExpensesActionReport(
            [FromQuery][Required] long empId,
            [FromQuery][Required] DateTime date)
        {
            try
            {
                var expensesQuery = HRMSDBContext.EmployeeExpenses.AsQueryable();

                expensesQuery = expensesQuery.Where(e => e.EmpId == empId);

                var dateStart = date.Date;
                var dateEnd = dateStart.AddDays(1).AddTicks(-1);
                expensesQuery = expensesQuery.Where(e => e.Date >= dateStart && e.Date <= dateEnd);

                var expenses = expensesQuery.ToList();

                if (!expenses.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No expenses found for the given criteria.",
                        Result = new { message = "No expenses found." }
                    };
                }

                var employeeIds = expenses.Select(e => e.EmpId).Distinct().ToList();
                var employees = CommonDBContext.EmployeeInfos
                    .Where(e => employeeIds.Contains(e.EmpNo))
                    .ToList();

                // Load all needed expense types at once
                var expenseTypeIds = expenses.Select(e => e.ExpenseTypeId).Distinct().ToList();
                var expenseTypes = HRMSDBContext.ExpenseTypes
                    .Where(et => expenseTypeIds.Contains(et.ExpenseId))
                    .ToList();

                var expenseResponses = expenses.Select(e =>
                {
                    var emp = employees.FirstOrDefault(emp => emp.EmpNo == e.EmpId);
                    var expenseType = expenseTypes.FirstOrDefault(et => et.ExpenseId == e.ExpenseTypeId);

                    var employeeDisplay = emp != null ? $"{emp.EmpNo} - {emp.FirstName}" : e.EmpId.ToString();
                    var expenseTypeName = expenseType != null ? expenseType.ExpenseName : "Unknown Expense Type";

                    return new EmployeeExpenseResponse
                    {
                        Id = e.Id,
                        EmpId = e.EmpId,
                        EmployeeName = employeeDisplay,
                        BranchName = emp?.BranchCode ?? "Unknown Branch",
                        ExpenseTypeId = e.ExpenseTypeId,
                        ExpenseTypeName = expenseTypeName,  
                        Date = e.Date,
                        Amount = e.Amount,
                        Remarks = e.Remarks ?? string.Empty,
                        Status = e.Status,
                        Attachment =e.Attachment
                    };
                }).ToList();

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Expenses fetched successfully.",
                    Result = expenseResponses
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = new { message = "An error occurred while processing your request." }
                };
            }
        }
        [HttpPost]
        [ActionName("AddApproveExpense")]
        [Authorize]
        public VivifyResponse<string> AddApproveExpense([FromBody] ApproveExpenseRequest request)
        {
            try
            {

                var dateStart = request.Date.Date;
                var dateEnd = dateStart.AddDays(1).AddTicks(-1);


                var expense = HRMSDBContext.EmployeeExpenses.FirstOrDefault(e =>
                    e.EmpId == request.EmpId &&
                    e.ExpenseTypeId == request.ExpenseTypeId &&
                    e.Date >= dateStart && e.Date <= dateEnd);

                if (expense == null)
                {
                    return new VivifyResponse<string>
                    {
                        StatusCode = 404,
                        StatusDesc = "Expense not found.",
                        Result = "No matching expense record was found."
                    };
                }


                expense.Status = true;
                expense.ApproveAmnt = request.ApprovedAmount;
                HRMSDBContext.SaveChanges();

                return new VivifyResponse<string>
                {
                    StatusCode = 200,
                    StatusDesc = "Expense approved successfully.",
                    Result = "Expense updated with approved amount."
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<string>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message}",
                    Result = "An error occurred while processing the approval."
                };
            }
        }

        [HttpGet]
        [ActionName("GetAllApprovedExpenses")]
        [Authorize]
        public VivifyResponse<object> GetAllApprovedExpenses(
            [FromQuery] DateOnly? fromDate = null,
            [FromQuery] DateOnly? toDate = null,
            [FromQuery] long? empId = null,
            [FromQuery] string? branchCode = null,
            [FromQuery] int? expenseTypeId = null)
        {
            try
            {
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(x => x.Type == "CompanyID")?.Value);

                Console.WriteLine($"Params: fromDate={fromDate}, toDate={toDate}, empId={empId}, branchCode={branchCode}, expenseTypeId={expenseTypeId}, companyId={companyId}");

                var expensesQuery = from exp in HRMSDBContext.EmployeeExpenses
                                    where exp.Status == true
                                    select exp;

                if (fromDate.HasValue && toDate.HasValue)
                {
                    var fromDateTime = fromDate.Value.ToDateTime(TimeOnly.MinValue);
                    var toDateTime = toDate.Value.ToDateTime(TimeOnly.MaxValue);
                    expensesQuery = expensesQuery.Where(e => e.Date >= fromDateTime && e.Date <= toDateTime);
                }
                else if (fromDate.HasValue)
                {
                    var fromDateTime = fromDate.Value.ToDateTime(TimeOnly.MinValue);
                    expensesQuery = expensesQuery.Where(e => e.Date >= fromDateTime);
                }
                else if (toDate.HasValue)
                {
                    var toDateTime = toDate.Value.ToDateTime(TimeOnly.MaxValue);
                    expensesQuery = expensesQuery.Where(e => e.Date <= toDateTime);
                }

                if (empId.HasValue)
                {
                    expensesQuery = expensesQuery.Where(e => e.EmpId == empId.Value);
                }

                if (!string.IsNullOrEmpty(branchCode))
                {
                    expensesQuery = expensesQuery.Where(e => e.BranchId == branchCode);
                }

                if (expenseTypeId.HasValue)
                {
                    expensesQuery = expensesQuery.Where(e => e.ExpenseTypeId == expenseTypeId.Value);
                }

                var expenses = expensesQuery.ToList();
                Console.WriteLine($"Fetched {expenses.Count} approved expenses.");

                // Fetch related data
                var empIds = expenses.Select(e => e.EmpId).Distinct().ToList();
                var employeeInfos = CommonDBContext.EmployeeInfos
                    .Where(e => empIds.Contains(e.EmpNo))
                    .ToList();

                var branchCodes = expenses.Select(e => e.BranchId).Distinct().ToList();
                var branchInfos = CommonDBContext.MBranches
                    .Where(b => branchCodes.Contains(b.BranchCode))
                    .ToList();

                var expenseTypeIds = expenses.Select(e => e.ExpenseTypeId).Distinct().ToList();
                var expenseTypeInfos = HRMSDBContext.ExpenseTypes
                    .Where(et => expenseTypeIds.Contains(et.ExpenseId))
                    .ToList();


                var result = expenses.Select(exp => new
                {
                    exp.Id,
                    exp.EmpId,
                    EmpName = $"{exp.EmpId} - {employeeInfos.FirstOrDefault(e => e.EmpNo == exp.EmpId)?.FirstName ?? "Unknown"}",
                    BranchCode = exp.BranchId,
                    BranchName = branchInfos.FirstOrDefault(b => b.BranchCode == exp.BranchId)?.BranchDesc ?? "Unknown",
                    exp.ExpenseTypeId,
                    ExpenseName = expenseTypeInfos.FirstOrDefault(et => et.ExpenseId == exp.ExpenseTypeId)?.ExpenseName ?? "Unknown",
                    exp.Amount,
                    exp.ApproveAmnt,
                    exp.Date,
                    exp.Status,
                    exp.Remarks
                }).ToList();


                if (!result.Any())
                {
                    return new VivifyResponse<object>
                    {
                        StatusCode = 404,
                        StatusDesc = "No approved expenses found.",
                        Result = new { message = "No approved expenses matched the given criteria." }
                    };
                }

                return new VivifyResponse<object>
                {
                    StatusCode = 200,
                    StatusDesc = "Approved expenses fetched successfully.",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<object>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error: {ex.Message} | StackTrace: {ex.StackTrace}",
                    Result = new { message = "An error occurred while fetching approved expenses." }
                };
            }
        }
        [HttpGet]
        [ActionName("GetPaySlipData")]
        public async Task<VivifyResponse<PaySlipDto>> GetPaySlipData([FromQuery] string monthYear)
        {
            try
            {
                var companyClaim = User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value;
                var empClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value;

                int? companyId = null;
                long? empNo = null;

                if (int.TryParse(companyClaim, out int compId))
                    companyId = compId;

                if (long.TryParse(empClaim, out long empId))
                    empNo = empId;

                if (!empNo.HasValue)
                {
                    return new VivifyResponse<PaySlipDto>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing EmpNo in token.",
                        Result = null
                    };
                }

                // Validate monthYear format
                if (string.IsNullOrWhiteSpace(monthYear) ||
                    !DateTime.TryParseExact(monthYear, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return new VivifyResponse<PaySlipDto>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid month year format. Expected format: yyyy-MM",
                        Result = null
                    };
                }

                // Fetch Employee Info
                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == empNo.Value);

                if (employee == null)
                {
                    return new VivifyResponse<PaySlipDto>
                    {
                        StatusCode = 404,
                        StatusDesc = "Employee not found.",
                        Result = null
                    };
                }

                // Fetch Designation Description
                var designation = await CommonDBContext.MDesignations
                    .Where(d => d.DesignationId == employee.Designation &&
                                (!companyId.HasValue || d.CompanyId == companyId.Value))
                    .Select(d => d.DesignationDesc)
                    .FirstOrDefaultAsync();

                // Fetch Company Info
                var companyInfo = await CommonDBContext.CompanyInfo
                    .Where(c => c.CompanyId == companyId)
                    .Select(c => new { c.CompanyName, c.CompanyLogo })
                    .FirstOrDefaultAsync();

                string companyName = companyInfo?.CompanyName ?? "N/A";
                string companyLogoUrl = companyInfo?.CompanyLogo;

                // Fetch all PayComponents including their PayComponentType
                var payComponentsQuery = CommonDBContext.PayComponents
                    .Include(pc => pc.PayComponentType)
                    .Where(pc => (pc.PayComponentTypeId == 1 || pc.PayComponentTypeId == 2 || pc.PayComponentTypeId == 3) &&
                                 (!companyId.HasValue || pc.CompanyId == companyId));

                var payComponentList = await payComponentsQuery.ToListAsync();

                if (!payComponentList.Any())
                {
                    return new VivifyResponse<PaySlipDto>
                    {
                        StatusCode = 404,
                        StatusDesc = "No pay components defined for this company.",
                        Result = null
                    };
                }

                // Fetch Employee Monthly Data
                var empPayComponentsList = await CommonDBContext.Employee_MonthlyPayComponents
                    .Where(e => e.EmpId == empNo.Value &&
                                e.PayMonthYear == monthYear &&
                                payComponentList.Select(p => p.PayComponentId).Contains(e.PayComponentId))
                    .ToListAsync();

                // Map all pay components with dynamic Topic from PayComponentType
                var allPayComponents = payComponentList
                    .Select(pc => new PayComponentDto
                    {
                        PayComponentId = pc.PayComponentId,
                        PayComponentName = pc.PayComponentName ?? "N/A",
                        PayComponentTypeId = pc.PayComponentTypeId,
                        Amount = (decimal?)empPayComponentsList
                            .FirstOrDefault(ep => ep.PayComponentId == pc.PayComponentId)?.Amount ?? 0,
                        Topic = pc.PayComponentType?.PayComponentTypeName ?? "Unknown"
                    })
                    .ToList();

                if (!allPayComponents.Any())
                {
                    return new VivifyResponse<PaySlipDto>
                    {
                        StatusCode = 404,
                        StatusDesc = "No pay component data found for this employee and month.",
                        Result = null
                    };
                }

                // Define Earning Topics and Deduction Topics
                var earningTopics = new HashSet<string> { "Basic & Allowance", "Earnings" };
                var deductionTopics = new HashSet<string> { "Deduction", "Statutory Deduction" };

                // Split dynamically by topic name
                var earnings = allPayComponents
                    .Where(x => earningTopics.Contains(x.Topic) && x.Amount > 0)
                    .ToList();

                var deductions = allPayComponents
                    .Where(x => deductionTopics.Contains(x.Topic) && x.Amount < 0)
                    .ToList();

                // Fetch Payroll Summary
                var payrollSummary = await CommonDBContext.EmployeeMonthlyPayrollSummaries
                    .FirstOrDefaultAsync(p => p.EmpId == empNo.Value && p.PayMonthYear == monthYear);

                if (payrollSummary == null)
                {
                    return new VivifyResponse<PaySlipDto>
                    {
                        StatusCode = 404,
                        StatusDesc = "Payroll summary not found for this month.",
                        Result = null
                    };
                }

                // Build Result
                var result = new PaySlipDto
                {
                    EmployeeId = employee.Id,
                    CompanyId = companyId ?? 0,
                    EmployeeCode = employee.EmpNo,
                    FirstName = employee.FirstName,
                    LastName = employee.LastName,
                    Designation = designation ?? "N/A",
                    UAN_No = employee.UAN_No,
                    CompanyName = companyName,
                    CompanyLogo = companyLogoUrl,
                    PayMonthYear = monthYear,
                    BankAccountNumber = employee.BankNo,  
                    BankName = employee.BankName,
                    TotalEarnings = payrollSummary.TotalEarnings,
                    TotalOtherDeductions = payrollSummary.TotalOtherDeductions,
                    NetPay = payrollSummary.NetPay,

                    Earnings = earnings,
                    Deductions = deductions,
                    AllPayComponents = allPayComponents
                };

                return new VivifyResponse<PaySlipDto>
                {
                    StatusCode = 200,
                    StatusDesc = "Payslip data retrieved successfully.",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<PaySlipDto>
                {
                    StatusCode = 500,
                    StatusDesc = $"An error occurred: {ex.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("GetLatestPaySlip")]
        public async Task<VivifyResponse<DynamicPaySlipDto>> GetLatestPaySlip()
        {
            try
            {
                var companyClaim = User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value;
                var empClaim = User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value;

                int? companyId = null;
                long? empNo = null;

                if (int.TryParse(companyClaim, out int compId))
                    companyId = compId;

                if (long.TryParse(empClaim, out long empId))
                    empNo = empId;

                if (!empNo.HasValue)
                {
                    return new VivifyResponse<DynamicPaySlipDto>
                    {
                        StatusCode = 400,
                        StatusDesc = "Invalid or missing EmpNo in token.",
                        Result = null
                    };
                }

                // Automatically determine latest monthYear
                var now = DateTime.Now;
                var latestMonthYear = now.ToString("yyyy-MM");

                // Fetch Employee Info
                var employee = await CommonDBContext.EmployeeInfos
                    .FirstOrDefaultAsync(e => e.EmpNo == empNo.Value);

                string employeeName = "N/A";
                string empCode = "N/A";
                string position = "N/A";
                string uanNumber = "N/A";

                if (employee != null)
                {
                    employeeName = $"{employee.FirstName} {employee.LastName}";
                    empCode = employee.EmpNo.ToString();
                    uanNumber = employee.UAN_No?.ToString() ?? "-";

                    // Fetch Designation Description
                    var designation = await CommonDBContext.MDesignations
                        .Where(d => d.DesignationId == employee.Designation &&
                                    (!companyId.HasValue || d.CompanyId == companyId.Value))
                        .Select(d => d.DesignationDesc)
                        .FirstOrDefaultAsync();

                    position = designation ?? "N/A";
                }

                // Fetch Company Info
                var companyInfo = await CommonDBContext.CompanyInfo
                    .Where(c => c.CompanyId == companyId)
                    .Select(c => new { c.CompanyName, c.CompanyLogo })
                    .FirstOrDefaultAsync();

                string companyName = companyInfo?.CompanyName ?? "N/A";
                string companyLogoUrl = companyInfo?.CompanyLogo ?? "";

                // Fetch Payroll Summary (may be null)
                var payrollSummary = await CommonDBContext.EmployeeMonthlyPayrollSummaries
                    .Where(p => p.EmpId == empNo.Value && p.PayMonthYear == latestMonthYear)
                    .FirstOrDefaultAsync();

                decimal? grossSalary = payrollSummary?.TotalEarnings ?? 0;
                decimal? totalDeduction = payrollSummary?.TotalOtherDeductions ?? 0;
                decimal? netSalary = payrollSummary?.NetPay ?? 0;

                // Fetch all PayComponents (labels) for this company
                var payComponentsQuery = CommonDBContext.PayComponents
                    .Where(pc => (pc.PayComponentTypeId == 1 || pc.PayComponentTypeId == 2 || pc.PayComponentTypeId == 3) &&
                                 (!companyId.HasValue || pc.CompanyId == companyId));

                var payComponentList = await payComponentsQuery.ToListAsync();

                if (!payComponentList.Any())
                {
                    return new VivifyResponse<DynamicPaySlipDto>
                    {
                        StatusCode = 404,
                        StatusDesc = "No pay components defined for this company.",
                        Result = null
                    };
                }

                // Fetch Monthly Data for current employee
                var empPayComponentsList = await CommonDBContext.Employee_MonthlyPayComponents
                    .Where(e => e.EmpId == empNo.Value &&
                                e.PayMonthYear == latestMonthYear &&
                                payComponentList.Select(p => p.PayComponentId).Contains(e.PayComponentId))
                    .ToListAsync();

                // Map All Pay Components
                var earnings = new List<DynamicPayComponentDto>();
                var deductions = new List<DynamicPayComponentDto>();

                foreach (var pc in payComponentList)
                {
                    var amount = empPayComponentsList
                        .FirstOrDefault(ep => ep.PayComponentId == pc.PayComponentId)?.Amount;

                    var dto = new DynamicPayComponentDto
                    {
                        Label = pc.PayComponentName ?? "N/A",
                        Value = (decimal?)amount ?? 0
                    };

                    if (pc.PayComponentTypeId == 1 && dto.Value > 0)
                        earnings.Add(dto);
                    else if ((pc.PayComponentTypeId == 2 || pc.PayComponentTypeId == 3) && dto.Value < 0)
                        deductions.Add(new DynamicPayComponentDto
                        {
                            Label = pc.PayComponentName ?? "Unknown",
                            Value = Math.Abs((decimal)dto.Value)
                        });
                }

                // Add summary values as dynamic components if they don't exist
                if (!earnings.Any(e => e.Label == "Gross Salary"))
                {
                    earnings.Add(new DynamicPayComponentDto
                    {
                        Label = "Gross Salary",
                        Value = grossSalary
                    });
                }

                if (!deductions.Any(d => d.Label == "Total Deduction"))
                {
                    deductions.Add(new DynamicPayComponentDto
                    {
                        Label = "Total Deduction",
                        Value = totalDeduction
                    });
                }

                // Build Result
                var result = new DynamicPaySlipDto
                {
                    EmployeeName = employeeName,
                    EmpCode = empCode,
                    Position = position,
                    UAN_Number = uanNumber,
                    CompanyName = companyName,
                    CompanyLogo = companyLogoUrl,
                    PayMonthYear = latestMonthYear switch
                    {
                        "2025-01" => "January 2025",
                        "2025-02" => "February 2025",
                        "2025-03" => "March 2025",
                        "2025-04" => "April 2025",
                        "2025-05" => "May 2025",
                        "2025-06" => "June 2025",
                        "2025-07" => "July 2025",
                        "2025-08" => "August 2025",
                        "2025-09" => "September 2025",
                        "2025-10" => "October 2025",
                        "2025-11" => "November 2025",
                        "2025-12" => "December 2025",
                        _ => latestMonthYear
                    },
                    Earnings = earnings,
                    Deductions = deductions,
                    Summary = new DynamicPaySlipSummaryDto
                    {
                        GrossSalary = grossSalary,
                        TotalDeduction = totalDeduction,
                        NetSalary = netSalary
                    }
                };

                return new VivifyResponse<DynamicPaySlipDto>
                {
                    StatusCode = 200,
                    StatusDesc = "Payslip data retrieved successfully.",
                    Result = result
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<DynamicPaySlipDto>
                {
                    StatusCode = 500,
                    StatusDesc = $"An error occurred: {ex.Message}",
                    Result = null
                };
            }
        }
        [HttpGet]
        [ActionName("CalculateBenifitAmount")]
        public VivifyResponse<decimal> CalculateBenifitAmount([FromQuery] int payComponentId)
        {
            try
            {
                // Extract EmpNo and CompanyID from JWT token
                long empNo = Convert.ToInt64(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "EmpNo")?.Value);
                int companyId = Convert.ToInt32(HttpContext.User.Claims.FirstOrDefault(c => c.Type == "CompanyID")?.Value);

                var payComponent = CommonDBContext.PayComponents
                    .FirstOrDefault(pc => pc.PayComponentId == payComponentId && pc.CompanyId == companyId);

                if (payComponent == null)
                {
                    return new VivifyResponse<decimal>
                    {
                        StatusCode = 404,
                        StatusDesc = "Pay component not found.",
                        Result = 0
                    };
                }

                if (!payComponent.IsFormula.GetValueOrDefault())
                {
                    return new VivifyResponse<decimal>
                    {
                        StatusCode = 400,
                        StatusDesc = "This pay component doesn't use a formula.",
                        Result = 0
                    };
                }

                if (string.IsNullOrWhiteSpace(payComponent.Formula))
                {
                    return new VivifyResponse<decimal>
                    {
                        StatusCode = 400,
                        StatusDesc = "No formula defined for this pay component.",
                        Result = 0
                    };
                }

                string formula = payComponent.Formula.Replace(" ", "");

                // Check if percentage is included
                bool hasPercentage = formula.Contains("%");
                string leftSide;
                decimal percentage = 100;

                if (hasPercentage)
                {
                    var parts = formula.Split('%');
                    leftSide = parts[0];
                    if (!decimal.TryParse(parts[1], out percentage))
                    {
                        return new VivifyResponse<decimal>
                        {
                            StatusCode = 400,
                            StatusDesc = "Invalid percentage value in formula.",
                            Result = 0
                        };
                    }
                }
                else
                {
                    leftSide = formula;
                }

                // Split formula into individual components
                var componentIds = leftSide.Split('+');

                List<int> payComponentIds = new();
                foreach (var idStr in componentIds)
                {
                    if (idStr.StartsWith("PayComponent_"))
                    {
                        string numPart = idStr.Substring("PayComponent_".Length);
                        if (!int.TryParse(numPart, out int compId))
                        {
                            return new VivifyResponse<decimal>
                            {
                                StatusCode = 400,
                                StatusDesc = $"Invalid component ID: {idStr}",
                                Result = 0
                            };
                        }
                        payComponentIds.Add(compId);
                    }
                    else if (int.TryParse(idStr, out int compId))
                    {
                        payComponentIds.Add(compId);
                    }
                    else
                    {
                        return new VivifyResponse<decimal>
                        {
                            StatusCode = 400,
                            StatusDesc = $"Invalid component ID: {idStr}",
                            Result = 0
                        };
                    }
                }

                // Fetch amounts from database
                List<decimal?> componentValues = new();

                foreach (var compId in payComponentIds)
                {
                    decimal? amount = GetEffectiveAmount(empNo, compId);
                    componentValues.Add(amount);
                }

                // Ensure all values are valid
                if (componentValues.Any(v => v == null))
                {
                    return new VivifyResponse<decimal>
                    {
                        StatusCode = 404,
                        StatusDesc = "One or more components returned no data.",
                        Result = 0
                    };
                }

                decimal totalAmount = componentValues.Sum(v => v.Value);
                decimal calculatedAmount = hasPercentage ? totalAmount * percentage / 100 : totalAmount;

                return new VivifyResponse<decimal>
                {
                    StatusCode = 200,
                    StatusDesc = "Amount calculated successfully.",
                    Result = calculatedAmount
                };
            }
            catch (Exception ex)
            {
                return new VivifyResponse<decimal>
                {
                    StatusCode = 500,
                    StatusDesc = $"Error calculating formula amount: {ex.Message}",
                    Result = 0
                };
            }
        }
        private decimal? GetEffectiveAmount(long empNo, int payComponentId)
        {
            var benefit = CommonDBContext.EmployeeBenefits
                .FirstOrDefault(eb => eb.EmpId == empNo &&
                                    eb.PayComponentId == payComponentId &&
                                    eb.IsActive == true);

            if (benefit != null)
                return benefit.Amount;

            var deduction = CommonDBContext.EmployeeDeductions
                .FirstOrDefault(ed => ed.EmpId == empNo &&
                                    ed.PayComponentID == payComponentId &&
                                    ed.Active == 1);

            if (deduction != null)
                return deduction.MonthlyRepayment_Amt;

            return null; // Not found
        }

    }
}
