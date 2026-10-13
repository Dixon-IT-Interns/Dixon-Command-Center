/* eslint-disable react-hooks/set-state-in-effect */
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  AlertCircle, ArrowLeft, CheckCircle2, ClipboardList, Loader2,
  Plus, Send, ToggleLeft, ToggleRight, Trash2, Upload, X
} from "lucide-react";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import ProcessTracker from "../../components/contributor/ProcessTracker";
import "./DailyReport.css";

const EMPTY_FORM = {
  monthPlan: "", monthActual: "",
  productionPlan: "", productionActual: "",
  uphTarget: "", uphActual: "",
  upphTarget: "", upphInstalled: "", upphActual: "",
  cphTarget: "", cphActual: "",
  fpyTarget: "", fpyActual: "",
  ftyTarget: "", ftyActual: "",
  rtyTarget: "", rtyActual: "",
  osdTarget: "",
  osdReportingDateValue: "", osdReportingDatePercent: null,
  osdMtdValue: "", osdMtdPercent: null,
  otTarget: "",
  plannedOTManhours: null, unplannedOTManhours: null, actualOTManhours: "",
  openWOQty: "", over7DaysWOBalanceQty: "",
  dailyTRCInQty: "", dailyTRCOutQty: "",
  trcOverallFailureInflowPercent: "", trcLyingOver3DaysCr: "",
  issueDescription: "",
};

const GROUPS = [
  {
    title: "Production",
    rows: [
      ["Month Plan", "monthPlan", "Plan"],
      ["Month Plan", "monthActual", "Actual"],
      ["Production", "productionPlan", "Plan"],
      ["Production", "productionActual", "Actual"],
      ["UPH", "uphTarget", "Target"],
      ["UPH", "uphActual", "Actual"],
      ["UPPH", "upphTarget", "Target"],
      ["UPPH", "upphInstalled", "Installed"],
      ["UPPH", "upphActual", "Actual"],
      ["CPH", "cphTarget", "Target"],
      ["CPH", "cphActual", "Actual"],
    ],
  },
  {
    title: "Quality",
    rows: [
      ["FPY", "fpyTarget", "Target"], ["FPY", "fpyActual", "Actual"],
      ["FTY", "ftyTarget", "Target"], ["FTY", "ftyActual", "Actual"],
      ["RTY", "rtyTarget", "Target"], ["RTY", "rtyActual", "Actual"],
    ],
  },
  {
    title: "Operations",
    rows: [
      ["OS&D — Reporting Date", "osdReportingDateValue", "Reporting Date"],
      ["OS&D", "osdTarget", "Target"],
      ["OS&D — Reporting Date %", "osdReportingDatePercent", "%"],
      ["OS&D — MTD", "osdMtdValue", "MTD"],
      ["OS&D — MTD %", "osdMtdPercent", "%"],
      ["OT", "otTarget", "Target"],
      ["Planned OT (Hrs)", "plannedOTManhours", "Planned"],
      ["Unplanned OT (Hrs)", "unplannedOTManhours", "Unplanned"],
      ["Actual OT (Hrs)", "actualOTManhours", "Actual"],
      ["Open WO (Qty)", "openWOQty", "Value"],
      [">7 Days WO (Qty)", "over7DaysWOBalanceQty", "Value"],
    ],
  },
  {
    title: "TRC",
    rows: [
      ["Daily TRC In (Qty)", "dailyTRCInQty", "Value"],
      ["Daily TRC Out (Qty)", "dailyTRCOutQty", "Value"],
      ["TRC Overall Failure Inflow %", "trcOverallFailureInflowPercent", "%"],
      ["TRC Lying >3 Days (Cr)", "trcLyingOver3DaysCr", "Value"],
    ],
  },
  { title: "Issue", rows: [["Issue Description", "issueDescription", ""]] },
];

const KPI_PLAN_FIELDS = [
  "productionPlan", "uphTarget", "upphTarget", "cphTarget",
  "fpyTarget", "ftyTarget", "rtyTarget", "osdTarget", "otTarget",
];

const FATP_IMPORT_PREVIEW_FIELDS = [
  ["Month Plan", "monthPlan"], ["Month Actual", "monthActual"],
  ["Production Plan", "productionPlan"], ["Production Actual", "productionActual"],
  ["UPH Target", "uphTarget"], ["UPH Actual", "uphActual"],
  ["UPPH Installed", "upphInstalled"], ["UPPH Actual", "upphActual"],
  ["OS&D Reporting Date", "osdReportingDateValue"],
  ["OS&D Reporting Date %", "osdReportingDatePercent"],
  ["OS&D MTD", "osdMtdValue"], ["OS&D MTD %", "osdMtdPercent"],
  ["Open WO Qty", "openWOQty"], [">7 Days WO Qty", "over7DaysWOBalanceQty"],
  ["Issue", "issueDescription"],
];

function today() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function asNumber(value) {
  if (value === "" || value === null || value === undefined) return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function asInteger(value) {
  const n = asNumber(value);
  return n === null ? null : Math.round(n);
}

function prefillPlanTargets(form, plan, editedFields = []) {
  if (!plan) return form;
  const edited = new Set(editedFields);
  const next = { ...form };
  for (const field of KPI_PLAN_FIELDS) {
    if (!edited.has(field) && (next[field] === "" || next[field] === null || next[field] === undefined))
      next[field] = plan[field] ?? "";
  }
  return next;
}

export default function DailyReport({ session, onSignOut }) {
  const navigate = useNavigate();
  const { customerId, categoryId } = useParams();
  const [customer, setCustomer] = useState(null);
  const [category, setCategory] = useState(null);
  const [lines, setLines] = useState([]);
  const [lineData, setLineData] = useState({});
  const [reportDate, setReportDate] = useState(today());
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState("");
  const [isImporting, setIsImporting] = useState(false);
  const [importPreview, setImportPreview] = useState(null);
  const [importDateConfirmed, setImportDateConfirmed] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const excelFileInput = useRef(null);
  const loadSequence = useRef(0);
  const entryIdSequence = useRef(0);
  const [loadedReportKey, setLoadedReportKey] = useState("");
  const currentReportKey = `${customerId}:${categoryId}:${reportDate}`;
  const reportLoaded = loadedReportKey === currentReportKey;
  const isSmtCategory = category?.categoryName?.trim().toUpperCase() === "SMT";
  const isFATPImport = category?.categoryName?.trim().toUpperCase() === "FATP";
  const reportGroups = useMemo(() => GROUPS.map((group) => (
    group.title === "Production"
      ? {
          ...group,
          rows: group.rows.filter(([, key]) => (
            isSmtCategory
              ? key !== "upphTarget" && key !== "upphInstalled" && key !== "upphActual"
              : key !== "cphTarget" && key !== "cphActual"
          )),
        }
      : group
  )), [isSmtCategory]);

  const fetchKpiPlan = async (lineId, modelId, date = reportDate) => {
    const query = new URLSearchParams({
      customerId,
      categoryId,
      lineId: String(lineId),
      modelId: String(modelId),
      reportDate: date,
    });
    return api.get(`/daily-reports/kpi-plan?${query.toString()}`);
  };

  const loadReport = async (showLoader = true) => {
    const sequence = ++loadSequence.current;
    if (showLoader) setLoading(true);
    setError("");
    try {
      const customers = await api.get("/catalog/customers");
      const foundCustomer = customers.find((x) => Number(x.customerId) === Number(customerId));
      const foundCategory = foundCustomer?.categories?.find((x) => Number(x.categoryId) === Number(categoryId));
      if (!foundCustomer || !foundCategory) throw new Error("Brand or category configuration was not found.");

      const statusRows = await api.get(`/daily-reports/line-status?customerId=${customerId}&categoryId=${categoryId}&reportDate=${reportDate}`);
      const catalogLines = Array.isArray(foundCategory.productionLines) ? foundCategory.productionLines : [];
      const mergedLines = catalogLines.map((line) => ({
        ...line,
        ...(statusRows.find((x) => Number(x.lineId) === Number(line.lineId)) || {}),
      }));

      const result = await Promise.all(mergedLines.map(async (line) => {
        const [config, entriesResponse] = await Promise.all([
          api.get(`/daily-reports/config?customerId=${customerId}&categoryId=${categoryId}&lineId=${line.lineId}&reportDate=${reportDate}`),
          api.get(`/daily-reports/entries?customerId=${customerId}&categoryId=${categoryId}&lineId=${line.lineId}&reportDate=${reportDate}`),
        ]);
        const saved = Array.isArray(entriesResponse) ? entriesResponse : [];
        const entries = await Promise.all(saved
          .filter((item) => item.modelId !== null && item.modelId !== undefined)
          .map(async (item) => {
            let plan = null;
            let planStatus;
            let planError = "";
            try {
              const response = await fetchKpiPlan(line.lineId, item.modelId);
              plan = response?.plan || null;
              planStatus = plan ? "ready" : "missing";
            } catch (e) {
              planError = e.message || "Unable to load this monthly KPI plan.";
              planStatus = /more than one|ambiguous/i.test(planError) ? "ambiguous" : "error";
            }

            return {
              entryId: `saved-${item.dailyReportId}`,
              dailyReportId: item.dailyReportId,
              modelId: String(item.modelId),
              modelName: item.modelName || "Model",
              status: item.status || "DRAFT",
              isActive: item.isActive !== false,
              rejectionReason: item.rejectionReason || "",
              form: { ...EMPTY_FORM, ...item },
              initialForm: { ...EMPTY_FORM, ...item },
              isNew: false,
              plan,
              planStatus,
              planError,
              planEditedFields: [],
            };
          }));

        return [line.lineId, {
          isActive: line.isActive !== false,
          status: line.status || "NOT_STARTED",
          models: Array.isArray(config.models) ? config.models : [],
          entries,
        }];
      }));

      if (sequence !== loadSequence.current) return;
      setCustomer(foundCustomer);
      setCategory(foundCategory);
      setLines(mergedLines);
      setLineData(Object.fromEntries(result));
      setLoadedReportKey(`${customerId}:${categoryId}:${reportDate}`);
    } catch (e) {
      if (sequence === loadSequence.current)
        setError(e.message || "Unable to load daily report.");
    } finally {
      if (sequence === loadSequence.current) setLoading(false);
    }
  };

  useEffect(() => {
    loadReport();
    return () => {
      loadSequence.current += 1;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [customerId, categoryId, reportDate]);

  const updateEntry = (lineId, entryId, field, value) => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        status: "IN_PROGRESS",
        entries: current[lineId].entries.map((entry) => {
          if (entry.entryId !== entryId) return entry;
          const editedFields = new Set(entry.planEditedFields || []);
          if (KPI_PLAN_FIELDS.includes(field)) {
            if (asNumber(value) === asNumber(entry.initialForm?.[field]))
              editedFields.delete(field);
            else if (asNumber(value) !== asNumber(entry.form[field]))
              editedFields.add(field);
          }
          return {
            ...entry,
            status: entry.status === "APPROVED" ? entry.status : "DRAFT",
            form: { ...entry.form, [field]: value },
            planEditedFields: [...editedFields],
          };
        }),
      },
    }));
    setNotice("");
  };

  const setEntryPlan = (lineId, entryId, plan, planStatus, planError = "") => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        entries: current[lineId].entries.map((entry) => entry.entryId !== entryId
          ? entry
          : {
              ...entry,
              plan,
              planStatus,
              planError,
              form: entry.isNew ? prefillPlanTargets(entry.form, plan, entry.planEditedFields) : entry.form,
              initialForm: entry.isNew
                ? prefillPlanTargets(entry.initialForm || { ...EMPTY_FORM }, plan, entry.planEditedFields)
                : entry.initialForm,
            }),
      },
    }));
  };

  const addModel = (lineId, modelId) => {
    if (!modelId) return;
    const line = lineData[lineId];
    if (line?.entries.some((entry) => String(entry.modelId) === String(modelId))) {
      setError(`${line.lineName}: this model is already on the line.`);
      return;
    }
    const model = line?.models.find((item) => String(item.modelId) === String(modelId));
    if (!model) return;
    const entryId = `new-${lineId}-${modelId}-${++entryIdSequence.current}`;
    const sequence = loadSequence.current;
    setLineData((current) => {
      const currentLine = current[lineId];
      if (!currentLine || currentLine.entries.some((entry) => String(entry.modelId) === String(modelId))) return current;
      return {
        ...current,
        [lineId]: {
          ...currentLine,
          status: "IN_PROGRESS",
          entries: [...currentLine.entries, {
            entryId,
            dailyReportId: null,
            modelId: String(modelId),
            modelName: model.modelName,
            status: "DRAFT",
            isActive: true,
            rejectionReason: "",
            form: { ...EMPTY_FORM },
            initialForm: { ...EMPTY_FORM },
            isNew: true,
            plan: null,
            planStatus: "loading",
            planError: "",
            planEditedFields: [],
          }],
        },
      };
    });

    fetchKpiPlan(lineId, modelId).then(({ plan }) => {
      if (sequence === loadSequence.current)
        setEntryPlan(lineId, entryId, plan || null, plan ? "ready" : "missing");
    }).catch((e) => {
      if (sequence === loadSequence.current)
        setEntryPlan(
          lineId,
          entryId,
          null,
          /more than one|ambiguous/i.test(e.message || "") ? "ambiguous" : "error",
          e.message || "Unable to load this monthly KPI plan."
        );
    });
  };

  const removeModel = (lineId, entryId) => {
    setLineData((current) => ({
      ...current,
      [lineId]: {
        ...current[lineId],
        entries: current[lineId].entries.filter((entry) => entry.entryId !== entryId),
      },
    }));
  };

  const toggleLine = async (lineId) => {
    const line = lineData[lineId];
    if (!line) return;
    const next = !line.isActive;
    if (!next && !window.confirm("Mark this line inactive for this date? Its daily values will be cleared.")) return;
    setBusy(`toggle-${lineId}`); setError(""); setNotice("");
    try {
      await api.post("/daily-reports/line-status", {
        reportDate, customerId: Number(customerId), categoryId: Number(categoryId), lineId: Number(lineId), isActive: next,
      });
      await loadReport(false);
      setNotice(`${getLineName(lineId)} is now ${next ? "active" : "inactive"}.`);
    } catch (e) {
      setError(e.message || "Unable to update line status.");
    } finally { setBusy(""); }
  };

  const getLineName = (lineId) => lines.find((x) => Number(x.lineId) === Number(lineId))?.lineName || "Selected line";

  const buildPayload = (lineId, entry) => ({
    reportDate,
    customerId: Number(customerId), categoryId: Number(categoryId), lineId: Number(lineId), modelId: Number(entry.modelId),
    kpiPlanId: entry.plan?.planId ?? null,
    kpiPlanUpdatedAt: entry.plan?.updatedAt ?? null,
    planEdits: entry.plan
      ? (entry.planEditedFields || [])
          .filter((field) => KPI_PLAN_FIELDS.includes(field)
            && asNumber(entry.form[field]) !== asNumber(entry.plan[field]))
          .map((field) => ({ field, value: asNumber(entry.form[field]) }))
      : [],
    monthPlan: asNumber(entry.form.monthPlan), monthActual: asNumber(entry.form.monthActual),
    productionPlan: asNumber(entry.form.productionPlan), productionActual: asNumber(entry.form.productionActual),
    uphTarget: asNumber(entry.form.uphTarget), uphActual: asNumber(entry.form.uphActual),
    upphTarget: isSmtCategory ? null : asNumber(entry.form.upphTarget),
    upphInstalled: isSmtCategory ? null : asNumber(entry.form.upphInstalled),
    upphActual: isSmtCategory ? null : asNumber(entry.form.upphActual),
    cphTarget: isSmtCategory ? asNumber(entry.form.cphTarget) : null,
    cphActual: isSmtCategory ? asNumber(entry.form.cphActual) : null,
    fpyTarget: asNumber(entry.form.fpyTarget), fpyActual: asNumber(entry.form.fpyActual),
    ftyTarget: asNumber(entry.form.ftyTarget), ftyActual: asNumber(entry.form.ftyActual),
    rtyTarget: asNumber(entry.form.rtyTarget), rtyActual: asNumber(entry.form.rtyActual),
    osdTarget: asNumber(entry.form.osdTarget),
    osdReportingDateValue: asNumber(entry.form.osdReportingDateValue), osdReportingDatePercent: asNumber(entry.form.osdReportingDatePercent),
    osdMtdValue: asNumber(entry.form.osdMtdValue), osdMtdPercent: asNumber(entry.form.osdMtdPercent),
    otTarget: asNumber(entry.form.otTarget),
    plannedOTManhours: asNumber(entry.form.plannedOTManhours), unplannedOTManhours: asNumber(entry.form.unplannedOTManhours),
    actualOTManhours: asNumber(entry.form.actualOTManhours), openWOQty: asInteger(entry.form.openWOQty),
    over7DaysWOBalanceQty: asInteger(entry.form.over7DaysWOBalanceQty), dailyTRCInQty: asInteger(entry.form.dailyTRCInQty),
    dailyTRCOutQty: asInteger(entry.form.dailyTRCOutQty), trcOverallFailureInflowPercent: asNumber(entry.form.trcOverallFailureInflowPercent),
    trcLyingOver3DaysCr: asNumber(entry.form.trcLyingOver3DaysCr), issueDescription: entry.form.issueDescription?.trim() || null,
  });

  const saveLine = async (lineId, submit) => {
    const line = lineData[lineId];
    if (!line?.isActive) return setError("Activate the line before saving or submitting it.");
    if (!line.entries.length) return setError(`Add at least one model to ${getLineName(lineId)}.`);
    if (new Set(line.entries.map((entry) => String(entry.modelId))).size !== line.entries.length) {
      return setError(`${getLineName(lineId)} contains the same model more than once.`);
    }
    setBusy(`${submit ? "submit" : "save"}-${lineId}`); setError(""); setNotice("");
    try {
      for (const entry of line.entries) {
        if (!entry.modelId) throw new Error(`Select a model for every column in ${getLineName(lineId)}.`);
        await api.post(`/daily-reports/${submit ? "submit" : "draft"}`, buildPayload(lineId, entry));
      }
      await loadReport(false);
      setNotice(`${getLineName(lineId)} ${submit ? "submitted for approval" : "draft saved"}.`);
    } catch (e) {
      setError(e.message || "Unable to save the report.");
    } finally { setBusy(""); }
  };

  const importExcel = async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;

    const sequence = loadSequence.current;
    setIsImporting(true);
    setError("");
    setNotice("");

    try {
      const formData = new FormData();
      formData.append("file", file);
      formData.append("customerId", String(customerId));
      formData.append("categoryId", String(categoryId));
      formData.append("reportDate", reportDate);

      const preview = await api.postFormData(
        "/daily-reports/import/preview",
        formData
      );
      if (sequence !== loadSequence.current)
        throw new Error("The report context changed while Excel was being read. Please upload the file again.");

      if (isFATPImport) {
        if (!Array.isArray(preview.rows) || preview.rows.length === 0)
          throw new Error("The FATP production table did not contain any rows to review.");
        setImportPreview({
          ...preview,
          rows: preview.rows.map((row) => ({
            ...row,
            include: row.status === "matched" && !lineData[row.lineId]?.entries?.some(
              (entry) => Number(entry.modelId) === Number(row.modelId)
                && ["SUBMITTED", "APPROVED"].includes(entry.status)
            ),
            selectedLineId: row.lineId ? String(row.lineId) : "",
            selectedModelId: row.modelId ? String(row.modelId) : "",
          })),
        });
        setImportDateConfirmed(preview.reportDate === reportDate);
        setNotice("Excel preview ready. Review each FATP row; nothing has been saved.");
        return;
      }

      const pageLines = new Map(lines.map((line) => [Number(line.lineId), line]));
      const validationErrors = [];

      for (const imported of preview.data || []) {
        const line = pageLines.get(Number(imported.lineId));
        const lineState = lineData[imported.lineId];
        if (!line || !lineState) {
          validationErrors.push(`Line ${imported.lineNo} is not available on this report page.`);
          continue;
        }

        const modelAvailable = lineState.models.some(
          (model) => Number(model.modelId) === Number(imported.modelId)
        );
        if (!modelAvailable) {
          validationErrors.push(
            `Model ${imported.modelName} is not available for line ${imported.lineNo} on this page.`
          );
        }

        const existingEntry = lineState.entries.find(
          (entry) => Number(entry.modelId) === Number(imported.modelId)
        );
        if (existingEntry?.status === "APPROVED" || existingEntry?.status === "SUBMITTED") {
          validationErrors.push(
            `Model ${imported.modelName} on line ${imported.lineNo} has already been submitted and cannot be replaced.`
          );
        }
      }

      if (validationErrors.length) {
        throw new Error(validationErrors.join(" "));
      }

      const importedNewModels = (preview.data || []).filter((imported) => (
        !lineData[imported.lineId]?.entries.some(
          (entry) => Number(entry.modelId) === Number(imported.modelId)
        )
      ));
      const importedEntryIds = new Map(importedNewModels.map((imported) => [
        `${imported.lineId}:${imported.modelId}`,
        `import-${imported.lineId}-${imported.modelId}-${++entryIdSequence.current}`,
      ]));

      setLineData((current) => {
        const next = { ...current };
        const grouped = new Map();

        for (const imported of preview.data || []) {
          const lineId = Number(imported.lineId);
          const entries = grouped.get(lineId) || [];
          entries.push(imported);
          grouped.set(lineId, entries);
        }

        for (const [lineId, importedEntries] of grouped) {
          const currentLine = next[lineId];
          const entries = [...currentLine.entries];

          for (const imported of importedEntries) {
            const existingIndex = entries.findIndex(
              (entry) => Number(entry.modelId) === Number(imported.modelId)
            );
            if (existingIndex >= 0) {
              const existing = entries[existingIndex];
              const formValues = { ...imported.form };
              const editedFields = new Set(existing.planEditedFields || []);
              for (const field of KPI_PLAN_FIELDS) {
                if (formValues[field] === null || formValues[field] === undefined || formValues[field] === "")
                  delete formValues[field];
                else if (asNumber(formValues[field]) === asNumber(existing.initialForm?.[field]))
                  editedFields.delete(field);
                else
                  editedFields.add(field);
              }
              entries[existingIndex] = {
                ...existing,
                form: { ...existing.form, ...formValues },
                status: "DRAFT",
                planEditedFields: [...editedFields],
              };
            } else {
              const form = { ...EMPTY_FORM, ...imported.form };
              entries.push({
                entryId: importedEntryIds.get(`${lineId}:${imported.modelId}`),
                dailyReportId: null,
                modelId: String(imported.modelId),
                modelName: imported.modelName,
                status: "DRAFT",
                isActive: true,
                rejectionReason: "",
                form,
                initialForm: { ...EMPTY_FORM },
                isNew: true,
                plan: null,
                planStatus: "loading",
                planError: "",
                planEditedFields: KPI_PLAN_FIELDS.filter((field) => (
                  form[field] !== null && form[field] !== undefined && form[field] !== ""
                )),
              });
            }
          }

          next[lineId] = { ...currentLine, status: "IN_PROGRESS", entries };
        }

        return next;
      });

      for (const imported of importedNewModels) {
        const entryId = importedEntryIds.get(`${imported.lineId}:${imported.modelId}`);
        fetchKpiPlan(imported.lineId, imported.modelId).then(({ plan }) => {
          if (sequence === loadSequence.current)
            setEntryPlan(imported.lineId, entryId, plan || null, plan ? "ready" : "missing");
        }).catch((e) => {
          if (sequence === loadSequence.current)
            setEntryPlan(
              imported.lineId,
              entryId,
              null,
              /more than one|ambiguous/i.test(e.message || "") ? "ambiguous" : "error",
              e.message || "Unable to load this monthly KPI plan."
            );
        });
      }

      setNotice(
        `Excel imported successfully — ${preview.lines} lines, ${preview.models} models. Please review the data before submitting.`
      );
    } catch (e) {
      setError(
        e.message?.startsWith("Excel import failed")
          ? e.message
          : `Excel import failed: ${e.message || "Unable to read the workbook."}`
      );
    } finally {
      setIsImporting(false);
      event.target.value = "";
    }
  };

  const updateImportRow = (rowIndex, changes) => {
    setImportPreview((current) => current
      ? {
          ...current,
          rows: current.rows.map((row, index) => index === rowIndex ? { ...row, ...changes } : row),
        }
      : current);
  };

  const applyReviewedImport = () => {
    if (!importPreview) return;
    const needsDateConfirmation = importPreview.reportDate !== reportDate;
    if (needsDateConfirmation && !importDateConfirmed) {
      setError("Confirm the workbook report date before applying the FATP rows.");
      return;
    }
    if (importPreview.reportDate !== reportDate || loading || !reportLoaded) {
      setError("The report is still loading for the workbook date. Wait for loading to finish, then apply.");
      return;
    }

    const selected = importPreview.rows.filter((row) => row.include);
    if (!selected.length) {
      setError("Select at least one matched or manually mapped FATP row to apply.");
      return;
    }

    const normalizedCustomer = (value) => String(value || "").replace(/[^a-z0-9]/gi, "").toUpperCase();
    const errors = [];
    const mapped = selected.map((row) => {
      const lineId = Number(row.selectedLineId);
      const modelId = Number(row.selectedModelId);
      const line = lineData[lineId];
      const model = line?.models?.find((item) => Number(item.modelId) === modelId);
      if (!row.customer || normalizedCustomer(row.customer) !== normalizedCustomer(customer?.customerName)) {
        errors.push(`Excel row ${row.rowNumber} does not identify the selected brand.`);
      }
      if (!line || line.isActive === false || !model) {
        errors.push(`Excel row ${row.rowNumber} must be mapped to an active line and model for this report.`);
      }
      const existing = line?.entries?.find((entry) => Number(entry.modelId) === modelId);
      if (existing && ["SUBMITTED", "APPROVED"].includes(existing.status)) {
        errors.push(`Excel row ${row.rowNumber} targets ${line.lineName} / ${model?.modelName}, which is locked.`);
      }
      return { row, lineId, modelId, line, model, existing };
    });

    const duplicateKeys = new Set();
    const seenMappings = new Set();
    for (const item of mapped) {
      const key = `${item.lineId}:${item.modelId}`;
      if (seenMappings.has(key)) duplicateKeys.add(key);
      seenMappings.add(key);
    }
    if (duplicateKeys.size) {
      errors.push("The reviewed rows contain duplicate line/model mappings. Skip duplicate rows before applying.");
    }
    if (errors.length) {
      setError(errors.join(" "));
      return;
    }

    const importedRows = mapped.map(({ row, lineId, modelId, model }) => ({
      lineId,
      modelId,
      modelName: model.modelName,
      form: Object.fromEntries(Object.entries(row.form || {}).filter(([, value]) => value !== null && value !== undefined && value !== "")),
    }));
    const newModels = importedRows.filter((imported) => (
      !lineData[imported.lineId]?.entries.some((entry) => Number(entry.modelId) === imported.modelId)
    ));
    const newEntryIds = new Map(newModels.map((imported) => [
      `${imported.lineId}:${imported.modelId}`,
      `reviewed-import-${imported.lineId}-${imported.modelId}-${++entryIdSequence.current}`,
    ]));

    setLineData((current) => {
      const next = { ...current };
      const grouped = new Map();
      for (const imported of importedRows) {
        const entries = grouped.get(imported.lineId) || [];
        entries.push(imported);
        grouped.set(imported.lineId, entries);
      }

      for (const [lineId, imports] of grouped) {
        const currentLine = next[lineId];
        if (!currentLine) continue;
        const entries = [...currentLine.entries];
        for (const imported of imports) {
          const index = entries.findIndex((entry) => Number(entry.modelId) === imported.modelId);
          if (index >= 0) {
            const existing = entries[index];
            const form = { ...imported.form };
            const editedFields = new Set(existing.planEditedFields || []);
            for (const field of KPI_PLAN_FIELDS) {
              if (!(field in form)) continue;
              if (asNumber(form[field]) === asNumber(existing.initialForm?.[field]))
                editedFields.delete(field);
              else
                editedFields.add(field);
            }
            entries[index] = {
              ...existing,
              form: { ...existing.form, ...form },
              planEditedFields: [...editedFields],
            };
          } else {
            const form = { ...EMPTY_FORM, ...imported.form };
            entries.push({
              entryId: newEntryIds.get(`${lineId}:${imported.modelId}`),
              dailyReportId: null,
              modelId: String(imported.modelId),
              modelName: imported.modelName,
              status: "DRAFT",
              isActive: true,
              rejectionReason: "",
              form,
              initialForm: { ...EMPTY_FORM },
              isNew: true,
              plan: null,
              planStatus: "loading",
              planError: "",
              planEditedFields: KPI_PLAN_FIELDS.filter((field) => (
                form[field] !== null && form[field] !== undefined && form[field] !== ""
              )),
            });
          }
        }
        next[lineId] = { ...currentLine, status: "IN_PROGRESS", entries };
      }
      return next;
    });

    const requestSequence = loadSequence.current;
    for (const imported of newModels) {
      const entryId = newEntryIds.get(`${imported.lineId}:${imported.modelId}`);
      fetchKpiPlan(imported.lineId, imported.modelId).then(({ plan }) => {
        if (requestSequence === loadSequence.current)
          setEntryPlan(imported.lineId, entryId, plan || null, plan ? "ready" : "missing");
      }).catch((e) => {
        if (requestSequence === loadSequence.current)
          setEntryPlan(
            imported.lineId,
            entryId,
            null,
            /more than one|ambiguous/i.test(e.message || "") ? "ambiguous" : "error",
            e.message || "Unable to load this monthly KPI plan."
          );
      });
    }

    setImportPreview(null);
    setImportDateConfirmed(false);
    setError("");
    setNotice(`Applied ${importedRows.length} reviewed FATP row(s) to the form. Review and save or submit manually.`);
  };

  const columns = useMemo(() => {
    const result = [];
    lines.forEach((line) => (lineData[line.lineId]?.entries || []).forEach((entry) => result.push({ lineId: line.lineId, entry })));
    return result;
  }, [lines, lineData]);

  const completed = lines.filter((line) => ["SUBMITTED", "APPROVED", "INACTIVE"].includes(lineData[line.lineId]?.status)).length;

  return (
    <ContributorLayout user={session?.user} onSignOut={onSignOut}>
      <main className="daily-report-page">
        <div className="daily-report-content">
          <ProcessTracker activeStep="report" customerId={customerId} categoryId={categoryId} />

          <button className="report-back" onClick={() => navigate(`/contributor/brands/${customerId}/categories`)}>
            <ArrowLeft size={16} /> Back to categories
          </button>

          <div className="report-breadcrumb">{customer?.customerName || "Brand"}<span>›</span>{category?.categoryName || "FATP"}<span>›</span><strong>Daily Report</strong></div>

          <header className="report-title-row">
            <div className="report-title-icon"><ClipboardList size={25} /></div>
            <div><h1>{category?.categoryName || "Daily"} Daily Report</h1><p>Plan targets load from the monthly KPI plan; daily actuals remain date-specific.</p></div>
            <button
              className="secondary-action report-excel-upload"
              type="button"
              style={{ marginLeft: "auto", flexShrink: 0 }}
              disabled={isImporting || Boolean(busy)}
              onClick={() => excelFileInput.current?.click()}
            >
              {isImporting ? <Loader2 size={14} className="spin" /> : <Upload size={14} />}
              {isImporting ? "Reading Excel..." : "Upload Excel"}
            </button>
            <input
              ref={excelFileInput}
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              hidden
              onChange={importExcel}
            />
          </header>

          <div className="report-context-bar">
            <div><span>BRAND</span><strong>{customer?.customerName || "—"}</strong></div>
            <div><span>CATEGORY</span><strong>{category?.categoryName || "—"}</strong></div>
            <label><span>REPORT DATE</span><input type="date" max={today()} value={reportDate} onChange={(e) => { loadSequence.current += 1; setReportDate(e.target.value); }} /></label>
            <div><span>PROGRESS</span><strong>{completed}/{lines.length} lines completed</strong></div>
          </div>

          {(error || notice) && (
            <div className={`report-message ${error ? "error" : "success"}`}>
              {error ? <AlertCircle size={18} /> : <CheckCircle2 size={18} />}
              <div><strong>{error ? "Please check the report" : /excel|applied/i.test(notice) ? "Import staged" : "Saved"}</strong><p>{error || notice}</p></div>
              <button onClick={() => { setError(""); setNotice(""); }}><X size={16} /></button>
            </div>
          )}

          {importPreview && (
            <section className="excel-review" aria-label="FATP Excel import review">
              <div className="excel-review-head">
                <div>
                  <span>FATP EXCEL PREVIEW</span>
                  <h2>Review imported rows</h2>
                  <p>Previewing {importPreview.rows.length} source row(s) from the FATP production table. Applying only stages values in this form; it does not save or submit.</p>
                </div>
                <button className="secondary-action" type="button" onClick={() => { setImportPreview(null); setImportDateConfirmed(false); }}>Cancel preview</button>
              </div>
              {importPreview.errors?.map((message) => <div className="excel-date-warning" key={message}>{message}</div>)}
              {importPreview.reportDate !== reportDate && (
                <>
                  <div className="excel-date-warning">Changing the report date reloads its saved values. Save any current draft edits before continuing.</div>
                  <label className="excel-date-confirm">
                    <input
                      type="checkbox"
                      checked={importDateConfirmed}
                      onChange={(event) => {
                        const confirmed = event.target.checked;
                        setImportDateConfirmed(confirmed);
                        if (confirmed) {
                          loadSequence.current += 1;
                          setReportDate(importPreview.reportDate);
                        }
                      }}
                    />
                    <span>Confirm changing this report to the workbook date, {importPreview.reportDate}.</span>
                  </label>
                </>
              )}
              {importPreview.reportDate === reportDate && loading && (
                <div className="excel-date-warning">Loading saved reports and plan data for {reportDate} before these rows can be applied…</div>
              )}
              <p className="excel-field-note">WIP Lying &gt;3 Days and WIP Lying &gt;7 Days have no equivalent Daily Report fields and are not imported. The workbook also has no FPY/FTY/RTY, overtime, or TRC columns; existing form values for absent columns are left unchanged.</p>
              <div className="excel-review-scroll">
                <table className="excel-review-table">
                  <thead><tr><th>Use</th><th>Source row</th><th>Original Excel values</th><th>Map to active line</th><th>Map to brand model</th><th>Values to stage</th><th>Review status</th></tr></thead>
                  <tbody>
                    {importPreview.rows.map((row, index) => {
                      const selectedLineId = Number(row.selectedLineId);
                      const selectedLine = lineData[selectedLineId];
                      const modelOptions = selectedLine?.models || [];
                      const customerMatches = row.customer
                        && row.customer.replace(/[^a-z0-9]/gi, "").toUpperCase()
                          === (customer?.customerName || "").replace(/[^a-z0-9]/gi, "").toUpperCase();
                      const canReview = Boolean(row.form) && Boolean(customerMatches);
                      const mappingReady = Boolean(selectedLine)
                        && selectedLine.isActive !== false
                        && modelOptions.some((model) => Number(model.modelId) === Number(row.selectedModelId));
                      const lockedEntry = selectedLine?.entries?.find(
                        (entry) => Number(entry.modelId) === Number(row.selectedModelId)
                          && ["SUBMITTED", "APPROVED"].includes(entry.status)
                      );
                      const statusLabel = lockedEntry
                        ? "Locked report"
                        : row.status === "matched"
                          ? "Matched"
                          : row.status === "duplicate"
                            ? "Duplicate — select at most one"
                            : row.status === "invalid"
                                ? "Invalid cells omitted"
                              : mappingReady
                                ? "Mapped — review"
                                : "Unmatched";
                      const lineSelect = (
                        <select
                          value={row.selectedLineId}
                          disabled={!canReview || !row.include}
                          onChange={(event) => {
                            const lineId = event.target.value;
                            const nextModels = lineData[Number(lineId)]?.models || [];
                            const retainsModel = nextModels.some((model) => String(model.modelId) === row.selectedModelId);
                            updateImportRow(index, {
                              selectedLineId: lineId,
                              selectedModelId: retainsModel ? row.selectedModelId : "",
                            });
                          }}
                        >
                          <option value="">Select line…</option>
                          {lines.filter((line) => lineData[line.lineId]?.isActive !== false).map((line) => <option key={line.lineId} value={line.lineId}>{line.lineName} · {line.sapLocation || "No SAP location"}</option>)}
                        </select>
                      );
                      return (
                        <tr key={`${row.rowNumber}-${index}`} className={`excel-row-${row.status}`}>
                          <td><input type="checkbox" aria-label={`Include Excel row ${row.rowNumber}`} checked={row.include} disabled={!canReview} onChange={(event) => updateImportRow(index, { include: event.target.checked })} /></td>
                          <td>{row.rowNumber}</td>
                          <td><strong>{row.lineNo || "—"}</strong><span>SAP: {row.sapLocation || "—"} · Brand: {row.customer || "—"}</span><span>Model: {row.modelName || "—"}</span></td>
                          <td>{lineSelect}</td>
                          <td>
                            <select
                              value={row.selectedModelId}
                              disabled={!canReview || !row.include || !selectedLine}
                              onChange={(event) => updateImportRow(index, { selectedModelId: event.target.value })}
                            >
                              <option value="">Select model…</option>
                              {modelOptions.map((model) => <option key={model.modelId} value={model.modelId}>{model.modelName}</option>)}
                            </select>
                          </td>
                          <td><div className="excel-staged-values">
                            {FATP_IMPORT_PREVIEW_FIELDS.filter(([, key]) => row.form?.[key] !== null && row.form?.[key] !== undefined && row.form?.[key] !== "")
                              .map(([label, key]) => <span key={key}><b>{label}:</b> {String(row.form[key])}</span>)}
                            {row.status === "invalid" && <span className="excel-invalid-note">Invalid cells are omitted; all other displayed values are staged.</span>}
                          </div></td>
                          <td><strong>{statusLabel}</strong>{row.reason && <span>{row.reason}</span>}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              <div className="excel-review-actions">
                <span>Included rows are staged in the form only. Use the existing Save Draft or Submit actions after reviewing.</span>
                <button className="primary-action" type="button" disabled={isImporting || loading || !reportLoaded || importPreview.reportDate !== reportDate} onClick={applyReviewedImport}>Apply reviewed rows to form</button>
              </div>
            </section>
          )}

          {loading ? (
            <div className="report-message info"><Loader2 size={18} className="spin" /><div><strong>Loading {category?.categoryName || "daily"} report</strong><p>Loading lines, models, saved values and monthly KPI plans…</p></div></div>
          ) : (
            <section className="matrix-shell">
              <div className="matrix-scroll">
                <table className="daily-matrix">
                  <thead>
                    <tr className="line-header-row">
                      <th className="parameter-head sticky-col" rowSpan="2">PARAMETERS</th>
                      {lines.map((line) => {
                        const count = Math.max(lineData[line.lineId]?.entries?.length || 0, 1);
                        return (
                          <th key={line.lineId} className="line-group-head" colSpan={count}>
                            <div className="line-head-inner">
                              <div><strong>{line.lineName}</strong><span>{line.sapLocation || "SAP location not set"}</span></div>
                              <button className={`matrix-line-toggle ${lineData[line.lineId]?.isActive === false ? "off" : ""}`} onClick={() => toggleLine(line.lineId)} disabled={busy === `toggle-${line.lineId}`}>
                                {lineData[line.lineId]?.isActive === false ? <ToggleLeft size={18} /> : <ToggleRight size={18} />}
                              </button>
                            </div>
                          </th>
                        );
                      })}
                    </tr>
                    <tr className="model-header-row">
                      {lines.map((line) => {
                        const data = lineData[line.lineId] || { entries: [], models: [] };
                        if (!data.entries.length) {
                          return <th key={line.lineId} className="model-head empty-model-head"><AddModelSelect models={data.models} onSelect={(id) => addModel(line.lineId, id)} disabled={!data.isActive} /></th>;
                        }
                        return data.entries.map((entry) => (
                          <th key={entry.entryId} className="model-head">
                            <div className="model-head-content">
                              <div><span>MODEL</span><strong>{entry.modelName}</strong></div>
                              {entry.planStatus === "loading" && <small className="plan-status loading"><Loader2 size={11} className="spin" /> Loading KPI plan…</small>}
                              {entry.planStatus === "missing" && <small className="plan-status missing" title="No monthly plan exists for this plant, customer, category, line, model and month.">{entry.isNew ? "No monthly plan; targets left blank." : "No active monthly plan; saved targets are preserved."}</small>}
                              {entry.planStatus === "error" && <small className="plan-status error" title={entry.planError}>KPI plan unavailable; daily report remains editable.</small>}
                              {entry.planStatus === "ambiguous" && <small className="plan-status ambiguous" title={entry.planError}>Multiple monthly plans match; targets not prefilled.</small>}
                              {entry.isNew && <button onClick={() => removeModel(line.lineId, entry.entryId)} title="Remove model"><Trash2 size={13} /></button>}
                            </div>
                          </th>
                        ));
                      })}
                    </tr>
                  </thead>
                  <tbody>
                    {reportGroups.map((group) => <MatrixGroup key={group.title} group={group} columns={columns} lineData={lineData} onChange={updateEntry} />)}
                  </tbody>
                </table>
              </div>

              <div className="matrix-add-model-bar">
                {lines.map((line) => {
                  const data = lineData[line.lineId] || { entries: [], models: [] };
                  const available = (data.models || []).filter((m) => !data.entries.some((e) => String(e.modelId) === String(m.modelId)));
                  if (!available.length) return null;
                  return <div className="line-add-model" key={line.lineId}><strong>{line.lineName}</strong><AddModelSelect models={available} onSelect={(id) => addModel(line.lineId, id)} disabled={!data.isActive} /></div>;
                })}
              </div>

              <div className="matrix-actions">
                <span>Plan targets prefill from the monthly KPI plan. Explicit target edits persist only when this report is saved.</span>
                <div className="action-groups">
                  {lines.map((line) => {
                    const data = lineData[line.lineId] || { entries: [] };
                    if (!data.entries.length) return null;
                    return <div className="line-action-group" key={line.lineId}><strong>{line.lineName}</strong>
                      <button className="secondary-action" disabled={busy === `save-${line.lineId}` || data.isActive === false} onClick={() => saveLine(line.lineId, false)}>{busy === `save-${line.lineId}` ? <Loader2 size={14} className="spin" /> : "Save Draft"}</button>
                      <button className="primary-action" disabled={busy === `submit-${line.lineId}` || data.isActive === false} onClick={() => saveLine(line.lineId, true)}>{busy === `submit-${line.lineId}` ? <Loader2 size={14} className="spin" /> : <><Send size={14} /> Submit</>}</button>
                    </div>;
                  })}
                </div>
              </div>
            </section>
          )}
        </div>
      </main>
    </ContributorLayout>
  );
}

function AddModelSelect({ models, onSelect, disabled }) {
  return <label className="add-model-control"><Plus size={13} /><select value="" onChange={(e) => onSelect(e.target.value)} disabled={disabled}><option value="">Add model</option>{models.map((model) => <option key={model.modelId} value={model.modelId}>{model.modelName}</option>)}</select></label>;
}

function MatrixGroup({ group, columns, lineData, onChange }) {
  return <>
    <tr className="group-row"><th className="sticky-col group-label">{group.title}</th>{columns.map(({ entry }) => <td className="group-spacer" key={`${group.title}-${entry.entryId}`} />)}</tr>
    {group.rows.map(([label, key, sub]) => <tr key={key} className={key === "issueDescription" ? "issue-row" : ""}>
      <th className="sticky-col parameter-label"><span>{label}</span>{sub && <small>{sub}</small>}</th>
      {columns.map(({ lineId, entry }) => {
        const disabled = entry.status === "APPROVED" || lineData[lineId]?.isActive === false;
        return <td className="matrix-cell" key={`${key}-${entry.entryId}`}>
          {key === "issueDescription" ? <textarea value={entry.form?.[key] ?? ""} disabled={disabled} placeholder="Issue / observation" onChange={(e) => onChange(lineId, entry.entryId, key, e.target.value)} />
            : <input type="number" min="0" step="0.01" value={entry.form?.[key] ?? ""} disabled={disabled} placeholder="—" onChange={(e) => onChange(lineId, entry.entryId, key, e.target.value)} />}
        </td>;
      })}
    </tr>)}
  </>;
}
