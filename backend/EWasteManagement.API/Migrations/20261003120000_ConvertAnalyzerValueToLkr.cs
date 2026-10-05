using EWasteManagement.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EWasteManagement.API.Migrations
{
    /// <summary>
    /// Data only (no schema change): the analyzer now estimates item value in Sri Lankan Rupees.
    /// Workflows analysed before that keep their result as JSON with "EstimatedValueUsd"; this
    /// renames the key to "EstimatedValueLkr" and converts the amount at 300 LKR per USD, the same
    /// rate the new LKR thresholds were derived from ($500 → Rs. 150,000; $300 → Rs. 90,000).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261003120000_ConvertAnalyzerValueToLkr")]
    public partial class ConvertAnalyzerValueToLkr : Migration
    {
        private const int LkrPerUsd = 300;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE collection_workflows
                SET analyzer_result_json = (analyzer_result_json - 'EstimatedValueUsd')
                    || jsonb_build_object('EstimatedValueLkr',
                        round((analyzer_result_json ->> 'EstimatedValueUsd')::numeric * {LkrPerUsd}, 2))
                WHERE analyzer_result_json ? 'EstimatedValueUsd';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE collection_workflows
                SET analyzer_result_json = (analyzer_result_json - 'EstimatedValueLkr')
                    || jsonb_build_object('EstimatedValueUsd',
                        round((analyzer_result_json ->> 'EstimatedValueLkr')::numeric / {LkrPerUsd}, 2))
                WHERE analyzer_result_json ? 'EstimatedValueLkr';
                """);
        }
    }
}
