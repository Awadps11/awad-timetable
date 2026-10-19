using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AwadTimetable.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Schools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PrincipalName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LogoPath = table.Column<string>(type: "TEXT", nullable: true),
                    WatermarkText = table.Column<string>(type: "TEXT", nullable: true),
                    CopyrightText = table.Column<string>(type: "TEXT", nullable: true),
                    AcademicYearStart = table.Column<int>(type: "INTEGER", nullable: false),
                    AcademicYearEnd = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    HasProjector = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasSmartBoard = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasComputers = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLab = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsGym = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLibrary = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMusicRoom = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArtRoom = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Floor = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Building = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PhotoPath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchoolId1 = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rooms_Schools_SchoolId1",
                        column: x => x.SchoolId1,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    DefaultPeriodsPerWeek = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 2),
                    MinPeriodsPerWeek = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    MaxPeriodsPerWeek = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5),
                    RequiresLab = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresSpecialRoom = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinStudentsPerClass = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxStudentsPerClass = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchoolId1 = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subjects_Schools_SchoolId1",
                        column: x => x.SchoolId1,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teachers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EmployeeNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NationalId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PhotoPath = table.Column<string>(type: "TEXT", nullable: true),
                    Specialization = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    HireDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    MaxPeriodsPerDay = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 6),
                    MaxPeriodsPerWeek = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    CanTeachMultipleGrades = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchoolId1 = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teachers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teachers_Schools_SchoolId1",
                        column: x => x.SchoolId1,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Timetables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AcademicYearStart = table.Column<int>(type: "INTEGER", nullable: false),
                    AcademicYearEnd = table.Column<int>(type: "INTEGER", nullable: false),
                    TermNumber = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Status = table.Column<byte>(type: "INTEGER", nullable: false, defaultValue: (byte)0),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalLessons = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    TotalConflicts = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchoolId1 = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timetables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Timetables_Schools_SchoolId1",
                        column: x => x.SchoolId1,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Classes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    GradeLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    Section = table.Column<char>(type: "TEXT", fixedLength: true, maxLength: 1, nullable: false),
                    StudentCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    MaxStudents = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SchoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchoolId1 = table.Column<Guid>(type: "TEXT", nullable: false),
                    HomeroomTeacherId1 = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefaultRoomId1 = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Classes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Classes_Rooms_DefaultRoomId1",
                        column: x => x.DefaultRoomId1,
                        principalTable: "Rooms",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Classes_Schools_SchoolId1",
                        column: x => x.SchoolId1,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Classes_Teachers_HomeroomTeacherId1",
                        column: x => x.HomeroomTeacherId1,
                        principalTable: "Teachers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubjectTeacher",
                columns: table => new
                {
                    SubjectsId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeachersId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectTeacher", x => new { x.SubjectsId, x.TeachersId });
                    table.ForeignKey(
                        name: "FK_SubjectTeacher_Subjects_SubjectsId",
                        column: x => x.SubjectsId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SubjectTeacher_Teachers_TeachersId",
                        column: x => x.TeachersId,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeacherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeacherId1 = table.Column<Guid>(type: "TEXT", nullable: false),
                    Day = table.Column<byte>(type: "INTEGER", nullable: false),
                    Period = table.Column<byte>(type: "INTEGER", nullable: false),
                    IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    TimetableId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherAvailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherAvailabilities_Teachers_TeacherId1",
                        column: x => x.TeacherId1,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeacherAvailabilities_Timetables_TimetableId",
                        column: x => x.TimetableId,
                        principalTable: "Timetables",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId1 = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeacherId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeacherId1 = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClassId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClassId1 = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RoomId1 = table.Column<Guid>(type: "TEXT", nullable: true),
                    Day = table.Column<byte>(type: "INTEGER", nullable: false),
                    Period = table.Column<byte>(type: "INTEGER", nullable: false),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<byte>(type: "INTEGER", nullable: false),
                    WeekNumber = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IsFixed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TimetableId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimetableId1 = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lessons_Classes_ClassId1",
                        column: x => x.ClassId1,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Lessons_Rooms_RoomId1",
                        column: x => x.RoomId1,
                        principalTable: "Rooms",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Lessons_Subjects_SubjectId1",
                        column: x => x.SubjectId1,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Lessons_Teachers_TeacherId1",
                        column: x => x.TeacherId1,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Lessons_Timetables_TimetableId1",
                        column: x => x.TimetableId1,
                        principalTable: "Timetables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Classes_DefaultRoomId1",
                table: "Classes",
                column: "DefaultRoomId1");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_HomeroomTeacherId1",
                table: "Classes",
                column: "HomeroomTeacherId1");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_SchoolId_GradeLevel_Section",
                table: "Classes",
                columns: new[] { "SchoolId", "GradeLevel", "Section" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Classes_SchoolId1",
                table: "Classes",
                column: "SchoolId1");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ClassId1",
                table: "Lessons",
                column: "ClassId1");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_RoomId1",
                table: "Lessons",
                column: "RoomId1");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_SubjectId1",
                table: "Lessons",
                column: "SubjectId1");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TeacherId1",
                table: "Lessons",
                column: "TeacherId1");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TimetableId_Day_Period_ClassId",
                table: "Lessons",
                columns: new[] { "TimetableId", "Day", "Period", "ClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TimetableId_Day_Period_RoomId",
                table: "Lessons",
                columns: new[] { "TimetableId", "Day", "Period", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TimetableId_Day_Period_TeacherId",
                table: "Lessons",
                columns: new[] { "TimetableId", "Day", "Period", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TimetableId1",
                table: "Lessons",
                column: "TimetableId1");

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_SchoolId_Code",
                table: "Rooms",
                columns: new[] { "SchoolId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_SchoolId1",
                table: "Rooms",
                column: "SchoolId1");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_Name",
                table: "Schools",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_SchoolId_Name",
                table: "Subjects",
                columns: new[] { "SchoolId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_SchoolId1",
                table: "Subjects",
                column: "SchoolId1");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectTeacher_TeachersId",
                table: "SubjectTeacher",
                column: "TeachersId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAvailabilities_TeacherId_Day_Period",
                table: "TeacherAvailabilities",
                columns: new[] { "TeacherId", "Day", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAvailabilities_TeacherId1",
                table: "TeacherAvailabilities",
                column: "TeacherId1");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAvailabilities_TimetableId",
                table: "TeacherAvailabilities",
                column: "TimetableId");

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_SchoolId_EmployeeNumber",
                table: "Teachers",
                columns: new[] { "SchoolId", "EmployeeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_SchoolId_LastName_FirstName",
                table: "Teachers",
                columns: new[] { "SchoolId", "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_SchoolId1",
                table: "Teachers",
                column: "SchoolId1");

            migrationBuilder.CreateIndex(
                name: "IX_Timetables_SchoolId_AcademicYearStart_AcademicYearEnd_TermNumber",
                table: "Timetables",
                columns: new[] { "SchoolId", "AcademicYearStart", "AcademicYearEnd", "TermNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timetables_SchoolId1",
                table: "Timetables",
                column: "SchoolId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "SubjectTeacher");

            migrationBuilder.DropTable(
                name: "TeacherAvailabilities");

            migrationBuilder.DropTable(
                name: "Classes");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropTable(
                name: "Timetables");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "Teachers");

            migrationBuilder.DropTable(
                name: "Schools");
        }
    }
}
