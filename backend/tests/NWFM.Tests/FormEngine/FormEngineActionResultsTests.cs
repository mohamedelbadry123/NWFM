namespace NWFM.Tests.Modules.FormEngine;

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using global::FormEngine.Api.Common;
using global::FormEngine.Application.Constants;
using NWFM.Shared.Results;

public sealed class FormEngineActionResultsTests
{
    [Theory]
    [InlineData(FormEngineErrors.Codes.FormNotFound, 404)]
    [InlineData(FormEngineErrors.Codes.VersionNotFound, 404)]
    [InlineData(FormEngineErrors.Codes.SubmissionNotFound, 404)]
    [InlineData(FormEngineErrors.Codes.FormDuplicateCode, 409)]
    [InlineData(FormEngineErrors.Codes.FieldTypeConflict, 409)]
    [InlineData(FormEngineErrors.Codes.FormNotPublished, 409)]
    [InlineData(FormEngineErrors.Codes.FileNotDeletable, 409)]
    [InlineData(FormEngineErrors.Codes.FileForbidden, 403)]
    [InlineData(FormEngineErrors.Codes.SchemaEmpty, 400)]
    [InlineData(FormEngineErrors.Codes.SubmissionAnswersInvalid, 400)]
    public void AFailureCodeChoosesItsStatus(string code, int expectedStatus)
    {
        var result = Result.Failure<string>(new Error(code, "message"));

        var action = result.ToActionResult().Should().BeOfType<ObjectResult>().Subject;

        action.StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public void AFailureBodySerializes_WithoutReadingTheValueThatWouldThrow()
    {
        var result = Result.Failure<string>(FormEngineErrors.Form.NotFound);

        var action = (ObjectResult)result.ToActionResult();

        // Serializing the Result itself would throw on Value; the explicit body is what avoids a 500.
        var json = FluentActions.Invoking(() => JsonSerializer.Serialize(action.Value)).Should().NotThrow().Subject;

        json.Should().Contain(FormEngineErrors.Codes.FormNotFound);
        json.Should().Contain("isSuccess");
    }

    [Fact]
    public void ASuccessIsReturnedWhole_SoClientsReadOneEnvelope()
    {
        var result = Result.Success("payload");

        var action = result.ToActionResult().Should().BeOfType<OkObjectResult>().Subject;

        action.Value.Should().BeSameAs(result);
    }

    [Fact]
    public void ACreateAnswersWith201()
    {
        var action = Result.Success("payload").ToCreatedResult().Should().BeOfType<ObjectResult>().Subject;

        action.StatusCode.Should().Be(201);
    }
}
