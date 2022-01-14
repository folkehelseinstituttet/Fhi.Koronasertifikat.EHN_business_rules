using System;
using FHICORC.BusinessRules.Tests.Utils;
using JsonLogic.Net;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FHICORC.BusinessRules.Tests.Enums;
using Newtonsoft.Json.Linq;

namespace FHICORC.BusinessRules.Tests
{
    public class BusinessRulesTest
    {
        private readonly JsonLogicEvaluator _evaluator;
        private readonly Dictionary<RuleUse, Dictionary<string, List<dynamic>>> _rules;

        private const int OneOfTwoMinDays = -21;
        private const int OneOfTwoMaxDays = -99;

        private const int TwoOfTwoMinDays = -7;
        private const int TwoOfTwoMaxDays = -9001; // Currently no max

        private const int ThreeOfTwoMinDays = 0;
        private const int ThreeOfTwoMaxDays = -9001; // Currently no max

        private const int ThreeOThreeMinDays = 0;
        private const int ThreeOfThreeMaxDays = -9001; // Currently no max

        private const int OneOfOneMinDays = -21;
        private const int OneOfOneTwoOfTwoTypeMinDays = -7;
        private const int OneOfOneMaxDays = -9001; // Currently no max

        private const int TwoOfOneMinDays = 0;
        private const int TwoOfOneMaxDays = -9001; // Currently no max

        private const int RecoveryMinDays = -11;
        private const int RecoveryMaxDays = -181; 

        private const int TestResultMaxHours = -24;

        private const string GeneralRules = "General";
        private const string TestRules = "Test";
        private const string VaccinationRules = "Vaccination";
        private const string RecoveryRules = "Recovery";

        public BusinessRulesTest()
        {
            _evaluator = new JsonLogicEvaluator(JsonLogicUtils.GetStringSupportedOperators());

            _rules = new Dictionary<RuleUse, Dictionary<string, List<dynamic>>>
            {
                [RuleUse.BorderControl] = new () { [GeneralRules] = new () },
                [RuleUse.Domestic] = new () { [GeneralRules] = new () }
            };

            var borderControlRulesJson = File.ReadAllText(@"Rules/Norwegian Border Control Rules.json");
            var allBorderControlRules = JArray.Parse(borderControlRulesJson).Cast<dynamic>().ToList();

            foreach (var rule in allBorderControlRules)
            {
                AddRule(_rules[RuleUse.BorderControl], rule);
            }

            var domesticRulesJson = File.ReadAllText(@"Rules/Norwegian Domestic Rules.json");
            var allDomesticRules = JArray.Parse(domesticRulesJson).Cast<dynamic>().ToList();

            foreach (var rule in allDomesticRules)
            {
                AddRule(_rules[RuleUse.Domestic], rule);
            }
        }

        private void AddRule(Dictionary<string, List<dynamic>> rules, dynamic rule)
        {
            var type = (string)rule.CertificateType;
            if (!rules.TryGetValue(type, out List<dynamic> rulesOfType))
            {
                rulesOfType = new List<dynamic>();
                rules[type] = rulesOfType;
            }

            rulesOfType.Add(rule);
        }

        [TestCase(RuleUse.BorderControl, GeneralRules, "GR-NO-")]
        [TestCase(RuleUse.BorderControl, VaccinationRules, "VR-NO-")]
        [TestCase(RuleUse.BorderControl, RecoveryRules, "RR-NO-")]
        [TestCase(RuleUse.BorderControl, TestRules, "TR-NO-")]
        [TestCase(RuleUse.Domestic, GeneralRules, "GR-NX-")]
        [TestCase(RuleUse.Domestic, VaccinationRules, "VR-NX-")]
        [TestCase(RuleUse.Domestic, RecoveryRules, "RR-NX-")]
        [TestCase(RuleUse.Domestic, TestRules, "TR-NX-")]
        public void Identifier_MatchesFormat(RuleUse ruleUse, string ruleType, string prefix)
        {
            Assert.True(_rules[ruleUse][ruleType].All(r => ((string)r.Identifier).StartsWith(prefix)));
        }

        [TestCase(RuleUse.BorderControl)]
        [TestCase(RuleUse.Domestic)]
        public void CertificateType_NotMatching(RuleUse ruleUse)
        {
            HashSet<string> validRuleTypes = new HashSet<string>(new[] { GeneralRules, VaccinationRules, RecoveryRules, TestRules });

            Assert.True(_rules[ruleUse].All(d => validRuleTypes.Contains(d.Key)));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of vaccines are not accepted at border control (even in otherwise valid period)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of vaccines are accepted domestically in valid period")]
        public void Vaccine_OneOfTwoDoses_InValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 2;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of vaccines are not accepted at border control (before valid period)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of vaccine not valid before 21 days")]
        public void Vaccine_OneOfTwoDoses_BeforeValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfTwoMinDays + 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 2;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of vaccines are not accepted at border control (after valid period)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of vaccine not valid after 99 days")]
        public void Vaccine_OneOfTwoDoses_AfterValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfTwoMaxDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 2;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of vaccines are not accepted at border control (unknown vaccine type)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "One of two doses of unknown vaccine type not valid")]
        public void Vaccine_OneOfTwoDoses_UnknownType(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "UNKNOWN_TYPE";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of Comirnaty valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of Comirnaty valid after 7 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_Comirnaty(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "EU/1/20/1528";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of Moderna valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of Moderna valid after 7 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_Moderna(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "EU/1/20/1507";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of Vaxzevria valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of Vaxzevria valid after 7 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_Vaxzevria(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "EU/1/21/1529";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of Covishield valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of Covishield valid after 7 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_Covishield(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "Covishield";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of CoronaVac valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of CoronaVac valid after 7 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_CoronaVac(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "CoronaVac";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of BBIBPCorV valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of BBIBPCorV valid after 7 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_BBIBPCorV(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "BBIBP-CorV";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of Covaxin valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of Covaxin valid after 7 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_Covaxin(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "Covaxin";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Two of two doses of vaccine not valid before 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Two of two doses of vaccine not valid before 7 days")]
        public void Vaccine_TwoOfTwoDoses_BeforeValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays + 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Two of two doses of vaccine not valid after max period")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Two of two doses of vaccine not valid after max period")]
        [Ignore("No max period for vaccines yet")]
        public void Vaccine_TwoOfTwoDoses_AfterValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMaxDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Two of two doses of unknown vaccine type not valid")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Two of two doses of unknown vaccine type not valid")]
        public void Vaccine_TwoOfTwoDoses_UnknownType(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "UNKNOWN_TYPE";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of Janssen valid after 21 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of Janssen valid after 21 days")]
        public void Vaccine_OneOfOneDoses_InValidPeriod_Janssen(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of one doses of Janssen valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of one doses of Janssen valid after 0 days")]
        public void Vaccine_TwoOfOneDoses_InValidPeriod_Janssen(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfOneMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of two doses of Janssen valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of two doses of Janssen valid after 0 days")]
        public void Vaccine_TwoOfTwoDoses_InValidPeriod_Janssen(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfOneMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Two of one doses of Janssen not valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Two of one doses of Janssen not valid after 0 days")]
        public void Vaccine_ThreeOfOneDoses_InValidPeriod_Janssen(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfOneMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Two of one doses of vaccine not valid before 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Two of one doses of vaccine not valid before 0 days")]
        public void Vaccine_TwoOfOneDoses_BeforeValidPeriod_Janssen(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfOneMinDays + 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Two of one doses of vaccine not valid after max period")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Two of one doses of vaccine not valid after max period")]
        [Ignore("No max period for vaccines yet")]
        public void Vaccine_TwoOfOneDoses_AfterValidPeriod_Janssen(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfOneMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Two of one doses of unknown vaccine type not valid")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Two of one doses of unknown vaccine type not valid")]
        public void Vaccine_TwoOfOneDoses_UnknownType(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(TwoOfOneMinDays - 1);
            vaccineData.payload.v[0].dn = 2;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "UNKNOWN_TYPE";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "One of one doses of vaccine not valid before 21 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "One of one doses of vaccine not valid before 21 days")]
        public void Vaccine_OneOfOneDoses_BeforeValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneMinDays + 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Comirnaty valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Comirnaty valid after 7 days")]
        public void Vaccine_OneOfOneDoses_TwoOfTwoType_InValidPeriod_Comirnaty(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneTwoOfTwoTypeMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1528";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Moderna valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Moderna valid after 7 days")]
        public void Vaccine_OneOfOneDoses_TwoOfTwoType_InValidPeriod_Moderna(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneTwoOfTwoTypeMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1507";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Vaxzevria valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Vaxzevria valid after 7 days")]
        public void Vaccine_OneOfOneDoses_TwoOfTwoType_InValidPeriod_Vaxzevria(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneTwoOfTwoTypeMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/21/1529";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Covishield valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Covishield valid after 7 days")]
        public void Vaccine_OneOfOneDoses_TwoOfTwoType_InValidPeriod_Covishield(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneTwoOfTwoTypeMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "Covishield";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) CoronaVac valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) CoronaVac valid after 7 days")]
        public void Vaccine_OneOfOneDoses_TwoOfTwoType_InValidPeriod_CoronaVac(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneTwoOfTwoTypeMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "CoronaVac";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) BBIBP-CorV valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) BBIBP-CorV valid after 7 days")]
        public void Vaccine_OneOfOneDoses_TwoOfTwoType_InValidPeriod_BBIBPCorV(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneTwoOfTwoTypeMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "BBIBP-CorV";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Covaxin valid after 7 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "One of one doses of (usually two dose) Covaxin valid after 7 days")]
        public void Vaccine_OneOfOneDoses_TwoOfTwoType_InValidPeriod_Covaxin(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneTwoOfTwoTypeMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "Covaxin";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "One of one doses of vaccine not valid after max period")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "One of one doses of vaccine not valid after max period")]
        [Ignore("No max period for vaccines yet")]
        public void Vaccine_OneOfOneDoses_AfterValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneMaxDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "EU/1/20/1525";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "One of one doses of unknown vaccine type not valid")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "One of one doses of unknown vaccine type not valid")]
        public void Vaccine_OneOfOneDoses_UnknownType(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(OneOfOneMinDays - 1);
            vaccineData.payload.v[0].dn = 1;
            vaccineData.payload.v[0].sd = 1;
            vaccineData.payload.v[0].mp = "UNKNOWN_TYPE";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of two doses of Comirnaty valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of two doses of Comirnaty valid after 0 days")]
        public void Vaccine_ThreeOfTwoDoses_InValidPeriod_Comirnaty(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "EU/1/20/1528";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of two doses of Moderna valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of two doses of Moderna valid after 0 days")]
        public void Vaccine_ThreeOfTwoDoses_InValidPeriod_Moderna(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "EU/1/20/1507";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of two doses of Vaxzevria valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of two doses of Vaxzevria valid after 0 days")]
        public void Vaccine_ThreeOfTwoDoses_InValidPeriod_Vaxzevria(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "EU/1/21/1529";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of two doses of Covishield valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of two doses of Covishield valid after 0 days")]
        public void Vaccine_ThreeOfTwoDoses_InValidPeriod_Covishield(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "Covishield";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of two doses of BBIBP-CorV valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of two doses of BBIBP-CorV valid after 0 days")]
        public void Vaccine_ThreeOfTwoDoses_InValidPeriod_BBIBPCorV(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "BBIBP-CorV";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of two doses of Covaxin valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of two doses of Covaxin valid after 0 days")]
        public void Vaccine_ThreeOfTwoDoses_InValidPeriod_Covaxin(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "Covaxin";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of two doses of CoronaVac valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of two doses of CoronaVac valid after 0 days")]
        public void Vaccine_ThreeOfTwoDoses_InValidPeriod_CoronaVac(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "CoronaVac";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Three of two doses of vaccine not valid before 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Three of two doses of vaccine not valid before 0 days")]
        public void Vaccine_ThreeOfTwoDoses_BeforeValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays + 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Three of two doses of vaccine not valid after max period")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Three of two doses of vaccine not valid after max period")]
        [Ignore("No max period for vaccines yet")]
        public void Vaccine_ThreeOfTwoDoses_AfterValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMaxDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Three of two doses of unknown vaccine type not valid")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Three of two doses of unknown vaccine type not valid")]
        public void Vaccine_ThreeOfTwoDoses_UnknownType(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfTwoMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 2;
            vaccineData.payload.v[0].mp = "UNKNOWN_TYPE";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of three doses of Comirnaty valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of three doses of Comirnaty valid after 0 days")]
        public void Vaccine_ThreeOfThreeDoses_InValidPeriod_Comirnaty(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOThreeMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;
            vaccineData.payload.v[0].mp = "EU/1/20/1528";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of three doses of Moderna valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of three doses of Moderna valid after 0 days")]
        public void Vaccine_ThreeOfThreeDoses_InValidPeriod_Moderna(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOThreeMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;
            vaccineData.payload.v[0].mp = "EU/1/20/1507";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of three doses of Vaxzevria valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of three doses of Vaxzevria valid after 0 days")]
        public void Vaccine_ThreeOfThreeDoses_InValidPeriod_Vaxzevria(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOThreeMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;
            vaccineData.payload.v[0].mp = "EU/1/21/1529";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of three doses of Covishield valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of three doses of Covishield valid after 0 days")]
        public void Vaccine_ThreeOfThreeDoses_InValidPeriod_Covishield(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOThreeMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;
            vaccineData.payload.v[0].mp = "Covishield";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Three of three doses of CoronaVac valid after 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Three of three doses of CoronaVac valid after 0 days")]
        public void Vaccine_ThreeOfThreeDoses_InValidPeriod_CoronaVac(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOThreeMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;
            vaccineData.payload.v[0].mp = "CoronaVac";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Three of three doses of vaccine not valid before 0 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Three of three doses of vaccine not valid before 0 days")]
        public void Vaccine_ThreeOfThreeDoses_BeforeValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOThreeMinDays + 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Three of three doses of vaccine not valid after max period")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Three of three doses of vaccine not valid after max period")]
        [Ignore("No max period for vaccines yet")]
        public void Vaccine_ThreeOfThreeDoses_AfterValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOfThreeMaxDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Three of three doses of unknown vaccine type not valid")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Three of three doses of unknown vaccine type not valid")]
        public void Vaccine_ThreeOfThreeDoses_UnknownType(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var vaccineData = GetVaccineData(ThreeOThreeMinDays - 1);
            vaccineData.payload.v[0].dn = 3;
            vaccineData.payload.v[0].sd = 3;
            vaccineData.payload.v[0].mp = "UNKNOWN_TYPE";

            var results = RunRules(GetRules(ruleUse, VaccinationRules), (JObject)vaccineData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllTrue,
            Description = "Recovery certificate valid after 11 days and before 180 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Recovery certificate valid after 11 days and before 180 days")]
        public void Recovery_InValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var recoveryData = GetRecoveryData(RecoveryMinDays - 1, 0, RecoveryMaxDays);

            var results = RunRules(GetRules(ruleUse, RecoveryRules), (JObject)recoveryData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Recovery certificate not valid before 10 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Recovery certificate not valid before 10 days")]
        public void Recovery_BeforeValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var recoveryData = GetRecoveryData(RecoveryMinDays + 1, 0, RecoveryMaxDays);

            var results = RunRules(GetRules(ruleUse, RecoveryRules), (JObject)recoveryData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AtLeastOneFalse,
            Description = "Recovery certificate not valid after 180 days")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Recovery certificate not valid after 180 days")]
        public void Recovery_AfterValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var recoveryData = GetRecoveryData(RecoveryMaxDays - 1, -RecoveryMaxDays, 0);

            var results = RunRules(GetRules(RuleUse.BorderControl, RecoveryRules), (JObject)recoveryData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllFalse,
            Description = "Test certificates are not accepted at border control (even in otherwise valid period)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "PCR based test certificate valid before 24 hours")]
        public void TestResult_Pcr_InValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var testData = GetTestData(TestResultMaxHours + 1);

            var results = RunRules(GetRules(ruleUse, TestRules), (JObject)testData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllFalse,
            Description = "Test certificates are not accepted at border control (even in otherwise valid period)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "RAT based test certificate of accepted manufacturer valid before 24 hours")]
        public void TestResult_Rat_InValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var testData = GetTestData(TestResultMaxHours + 1);
            testData.payload.t[0].tt = "LP217198-3";
            testData.payload.t[0].ma = "1833";

            var results = RunRules(GetRules(ruleUse, TestRules), (JObject)testData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllFalse,
            Description = "Test certificates are not accepted at border control (after valid period)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AllTrue,
            Description = "Test certificate not valid after 24 hours")]
        public void TestResult_AfterValidPeriod(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var testData = GetTestData(TestResultMaxHours - 1);

            var results = RunRules(GetRules(ruleUse, TestRules), (JObject)testData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        [TestCase(RuleUse.BorderControl, ExpectedResults.AllFalse,
            Description = "Test certificates are not accepted at border control (unknown test type)")]
        [TestCase(RuleUse.Domestic, ExpectedResults.AtLeastOneFalse,
            Description = "Test certificates with unknown test type not valid")]
        public void TestResult_InValidPeriod_UnknownType(RuleUse ruleUse, ExpectedResults expectedResults)
        {
            var testData = GetTestData(TestResultMaxHours + 1);
            testData.payload.t[0].tt = "LP217198-3";
            testData.payload.t[0].ma = "UNKNOWN_TYPE";

            var results = RunRules(GetRules(ruleUse, TestRules), (JObject)testData);

            Assert.True(ResultsMatches(results, expectedResults));
        }

        private IEnumerable<dynamic> GetRules(RuleUse ruleUse, string ruleType)
        {
            return _rules[ruleUse][GeneralRules].Concat(_rules[ruleUse][ruleType]);
        }

        private List<bool?> RunRules(IEnumerable<dynamic> rules, JObject data)
        {
            List<bool?> results = new List<bool?>();
            foreach (var rule in rules)
            {
                try
                {
                    var result = _evaluator.Apply((JToken)rule.Logic, data);

                    bool.TryParse(result.ToString(), out bool successful);
                    results.Add(successful);
                }
                catch (Exception)
                {
                    // OPEN
                    results.Add(null);
                }
            }

            return results;
        }

        private bool ResultsMatches(IEnumerable<bool?> results, ExpectedResults expected)
        {
            switch (expected)
            {
                case ExpectedResults.AllTrue:
                    return results.All(r => r == true);
                case ExpectedResults.AllFalse:
                    return results.All(r => r == false);
                case ExpectedResults.AtLeastOneFalse:
                    return results.Any(r => r == false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(expected), expected, null);
            }
        }

        private static dynamic GetVaccineData(int daysUntilVaccinationDate)
        {
            var template =
                @"{""payload"":{""ver"":null,""nam"":null,""dob"":""1990-01-01"",""v"":[{""tg"":null,""vp"":null,""mp"":""EU/1/20/1528"",""ma"":null,""dn"":2,""sd"":2,""dt"":""2021-01-01T00:00:00.0000000Z"",""co"":null,""is"":null,""ci"":null}],""t"":null,""r"":null},""external"":{""validationClock"":""2021-01-01T00:00:00.0000000Z"",""ValueSets"":null,""CountryCode"":""NO"",""Exp"":""2021-01-01T00:00:00.0000000Z"",""Iat"":""2021-01-01T00:00:00.0000000Z""}}";

            dynamic vaccineData = JObject.Parse(template);
            vaccineData.payload.v[0].dt = DateTime.UtcNow.AddDays(daysUntilVaccinationDate).ToString("O");
            vaccineData.external.validationClock = DateTime.UtcNow;
            vaccineData.external.Iat = DateTime.UtcNow;
            vaccineData.external.Exp = DateTime.UtcNow.AddDays(14);

            return vaccineData;
        }

        private static dynamic GetRecoveryData(int daysUntilFirstPositive, int daysUntilValidFrom, int daysUntilValidTo)
        {
            var template =
                @"{""payload"":{""ver"":null,""nam"":null,""dob"":""1990-01-01"",""v"":null,""t"":null,""r"":[{""tg"":""840539006"",""fr"":""2021-01-01T00:00:00.0000000Z"",""co"":null,""is"":null,""ci"":null,""df"":""01-01-2021"",""du"":""01-01-2021""}]},""external"":{""validationClock"":""2021-01-01T00:00:00.0000000Z"",""ValueSets"":null,""CountryCode"":""NO"",""Exp"":""2021-01-01T00:00:00.0000000Z"",""Iat"":""2021-01-01T00:00:00.0000000Z""}}";

            dynamic recoveryData = JObject.Parse(template);
            recoveryData.payload.r[0].fr = DateTime.UtcNow.AddDays(daysUntilFirstPositive).ToString("O");
            recoveryData.payload.r[0].df = DateTime.UtcNow.AddDays(daysUntilValidFrom).ToString("d");
            recoveryData.payload.r[0].du = DateTime.UtcNow.AddDays(daysUntilValidTo).ToString("d");
            recoveryData.external.validationClock = DateTime.UtcNow;
            recoveryData.external.Iat = DateTime.UtcNow;
            recoveryData.external.Exp = DateTime.UtcNow.AddDays(14);

            return recoveryData;
        }

        private static dynamic GetTestData(int hoursUntilSampleCollectedTime)
        {
            var template =
                @"{""payload"":{""ver"":null,""nam"":null,""dob"":""1990-01-01"",""v"":null,""t"":[{""tg"":""840539006"",""tt"":""LP6464-4"",""nm"":null,""ma"":null,""sc"":""2021-08-03T12:45:18.8094689Z"",""tr"":""260415000"",""tc"":null,""co"":null,""is"":null,""ci"":null}],""r"":null},""external"":{""validationClock"":""2021-08-04T12:45:18.8101657Z"",""ValueSets"":null,""CountryCode"":""NO"",""Exp"":""2021-08-18T12:45:18.8092633Z"",""Iat"":""2021-08-04T12:45:18.8091745Z""}}";

            dynamic testData = JObject.Parse(template);
            testData.payload.t[0].sc = DateTime.UtcNow.AddHours(hoursUntilSampleCollectedTime);
            testData.external.validationClock = DateTime.UtcNow;
            testData.external.Iat = DateTime.UtcNow;
            testData.external.Exp = DateTime.UtcNow.AddDays(14);

            return testData;
        }
    }
}
