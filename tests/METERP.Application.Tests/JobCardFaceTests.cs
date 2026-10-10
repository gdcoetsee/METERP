using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class JobCardFaceTests
{
    [Fact]
    public void Read_ShowsTokens_WhenNotesContainThem()
    {
        var face = JobCardFace.Read(
            "Access CSV | Team=FT | Status=Open | TRFid=FT16010 | JobCardNo=JC9 | CustomerOrderNo=PO-44 | Region=Limpopo");

        Assert.Equal("FT16010", face.TrfId);
        Assert.Equal("JC9", face.JobCardNo);
        Assert.Equal("FT", face.Team);
        Assert.Equal("PO-44", face.CustomerOrderNo);
        Assert.Equal("Limpopo", face.Region);
    }

    [Fact]
    public void Read_UsesDash_WhenTokensAreMissingOrEmpty()
    {
        var face = JobCardFace.Read("Access CSV | TRFid= | JobCardNo= | Team= | CustomerOrderNo= | Region= | Status=Open");

        Assert.Equal(JobCardFace.Blank, face.TrfId);
        Assert.Equal(JobCardFace.Blank, face.JobCardNo);
        Assert.Equal(JobCardFace.Blank, face.Team);
        Assert.Equal(JobCardFace.Blank, face.CustomerOrderNo);
        Assert.Equal(JobCardFace.Blank, face.Region);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Supervisor: only a name\nStillToInvoice: 0")]
    public void Read_UsesDash_WhenNotesHaveNoCardTokens(string? notes)
    {
        var face = JobCardFace.Read(notes);

        Assert.Equal(JobCardFace.Blank, face.TrfId);
        Assert.Equal(JobCardFace.Blank, face.JobCardNo);
        Assert.Equal(JobCardFace.Blank, face.Team);
        Assert.Equal(JobCardFace.Blank, face.CustomerOrderNo);
        Assert.Equal(JobCardFace.Blank, face.Region);
    }

    [Fact]
    public void From_Loads_WhenJobNotesAreEmpty()
    {
        var job = new Job
        {
            JobNumber = "   ",
            Title = "Panel",
            Notes = null,
            Description = "   "
        };

        var face = JobCardFace.From(job);

        Assert.Equal(JobCardFace.Blank, face.TrfId);
        Assert.Equal(JobCardFace.Blank, face.JobCardNo);
        Assert.Equal(JobCardFace.Blank, face.Team);
        Assert.Equal(JobCardFace.Blank, face.CustomerOrderNo);
        Assert.Equal(JobCardFace.Blank, face.Region);
    }

    [Fact]
    public void Read_ReadsColonLabels_FromImportedJobDescription()
    {
        const string description =
            "Type: Install | Team: FT | Region: Limpopo | Depot: Phalaborwa | JobCardNo: JC9 | CustomerOrderNo: PO-44 | QuoteNo: Q1 | SourceSheet: Weekly";

        var face = JobCardFace.Read(notes: null, description: description, jobNumber: "FT16010");

        Assert.Equal("FT16010", face.TrfId);
        Assert.Equal("JC9", face.JobCardNo);
        Assert.Equal("FT", face.Team);
        Assert.Equal("PO-44", face.CustomerOrderNo);
        Assert.Equal("Limpopo", face.Region);
    }

    [Fact]
    public void Read_PrefersNoteTokens_OverDescriptionAndJobNumber()
    {
        var face = JobCardFace.Read(
            "team=WS | trfid=FT9 | jobcardno=JC1, JC2 | region=JHB | customerorderno=CO-1",
            "Team: FT | Region: Limpopo | JobCardNo: JC9 | CustomerOrderNo: PO-44 | TRFid: FT16010",
            "FT16010");

        Assert.Equal("FT9", face.TrfId);
        Assert.Equal("JC1, JC2, JC9", face.JobCardNo);
        Assert.Equal("WS", face.Team);
        Assert.Equal("CO-1", face.CustomerOrderNo);
        Assert.Equal("JHB", face.Region);
    }

    [Fact]
    public void Read_FallsBackToJobNumber_WhenTrfTokenIsMissing()
    {
        var face = JobCardFace.Read("Team=FT", description: null, jobNumber: "FT16010");

        Assert.Equal("FT16010", face.TrfId);
        Assert.Equal("FT", face.Team);
        Assert.Equal(JobCardFace.Blank, face.JobCardNo);
        Assert.Equal(JobCardFace.Blank, face.Region);
        Assert.Equal(JobCardFace.Blank, face.CustomerOrderNo);
    }

    [Fact]
    public void Read_KeepsSpacedValues_AndIgnoresEmbeddedKeys()
    {
        var face = JobCardFace.Read(
            "SubRegion: Nowhere | NotTeam=XX | Team=Field Team | CustomerOrderNo=PO 100 | JobCardNo=JC 9 | JCs=JC3");

        Assert.Equal("Field Team", face.Team);
        Assert.Equal("PO 100", face.CustomerOrderNo);
        Assert.Equal("JC 9, JC3", face.JobCardNo);
        Assert.Equal(JobCardFace.Blank, face.Region);
    }
}
