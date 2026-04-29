using System.Net;
using System.Net.Http.Json;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

public interface IAssignmentManagementService
{
    Task<List<AssignmentSubmissionListItemDto>?> GetSubmissionsAsync();
    Task<List<TeamProjectMatchDto>?>             GetMatchingAsync();
    Task<AssignmentBoardDto?>                    GetAssignmentBoardAsync();
    Task<AssignTeamResult>                       AssignTeamAsync(int teamId, int projectId, bool force);
    Task<bool>                                   UnassignTeamAsync(int projectId);
    Task<AssignMentorResult>                     AssignMentorAsync(int projectId, int mentorId);
    Task<bool>                                   RemoveMentorAsync(int projectId, int mentorId);
    Task<bool>                                   PublishAsync(int academicYearId);
}

public class AssignmentManagementService : IAssignmentManagementService
{
    private readonly HttpClient _http;

    public AssignmentManagementService(HttpClient http) => _http = http;

    public async Task<List<AssignmentSubmissionListItemDto>?> GetSubmissionsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<AssignmentSubmissionListItemDto>>(
                "api/assignment-management/submissions");
        }
        catch { return null; }
    }

    public async Task<List<TeamProjectMatchDto>?> GetMatchingAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TeamProjectMatchDto>>(
                "api/assignment-management/matching");
        }
        catch { return null; }
    }

    public async Task<AssignmentBoardDto?> GetAssignmentBoardAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<AssignmentBoardDto>(
                "api/assignment-management/assignment-board");
        }
        catch { return null; }
    }

    public async Task<AssignTeamResult> AssignTeamAsync(int teamId, int projectId, bool force)
    {
        try
        {
            var res = await _http.PostAsJsonAsync(
                "api/assignment-management/assign-team",
                new AssignTeamRequest { TeamId = teamId, ProjectId = projectId, Force = force });

            if (res.IsSuccessStatusCode)
                return new AssignTeamResult { Outcome = AssignTeamOutcome.Success };

            if (res.StatusCode == HttpStatusCode.Conflict)
            {
                var conflict = await res.Content.ReadFromJsonAsync<AssignTeamConflictDto>();
                return new AssignTeamResult
                {
                    Outcome  = AssignTeamOutcome.Conflict,
                    Conflict = conflict
                };
            }

            return new AssignTeamResult
            {
                Outcome = AssignTeamOutcome.Error,
                Error   = await res.Content.ReadAsStringAsync()
            };
        }
        catch (Exception ex)
        {
            return new AssignTeamResult { Outcome = AssignTeamOutcome.Error, Error = ex.Message };
        }
    }

    public async Task<bool> UnassignTeamAsync(int projectId)
    {
        try
        {
            var res = await _http.PostAsJsonAsync(
                "api/assignment-management/unassign-team",
                new UnassignTeamRequest { ProjectId = projectId });
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<AssignMentorResult> AssignMentorAsync(int projectId, int mentorId)
    {
        try
        {
            var res = await _http.PostAsJsonAsync(
                "api/assignment-management/assign-mentor",
                new AssignMentorRequest { ProjectId = projectId, MentorId = mentorId });

            if (res.IsSuccessStatusCode)
                return new AssignMentorResult { Outcome = AssignMentorOutcome.Success };

            string body = await res.Content.ReadAsStringAsync();

            if (res.StatusCode == HttpStatusCode.Conflict)
            {
                var outcome = body.Contains("מקסימלי", StringComparison.OrdinalIgnoreCase)
                    ? AssignMentorOutcome.LimitReached
                    : AssignMentorOutcome.AlreadyAssigned;
                return new AssignMentorResult { Outcome = outcome, Error = body };
            }

            if (res.StatusCode == HttpStatusCode.BadRequest && body.Contains("מנטור", StringComparison.OrdinalIgnoreCase))
                return new AssignMentorResult { Outcome = AssignMentorOutcome.NotMentor, Error = body };

            return new AssignMentorResult { Outcome = AssignMentorOutcome.Error, Error = body };
        }
        catch (Exception ex)
        {
            return new AssignMentorResult { Outcome = AssignMentorOutcome.Error, Error = ex.Message };
        }
    }

    public async Task<bool> RemoveMentorAsync(int projectId, int mentorId)
    {
        try
        {
            var res = await _http.PostAsJsonAsync(
                "api/assignment-management/remove-mentor",
                new RemoveMentorRequest { ProjectId = projectId, MentorId = mentorId });
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> PublishAsync(int academicYearId)
    {
        try
        {
            var res = await _http.PostAsJsonAsync(
                "api/assignment-management/publish",
                new PublishAssignmentsRequest { AcademicYearId = academicYearId });
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
