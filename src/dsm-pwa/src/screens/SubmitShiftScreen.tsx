import { useState, useEffect, useRef } from "react";
import { useAuth } from "../contexts/AuthContext";
import { useSubmissionService } from "../hooks/useSubmissionService";
import { db, type DraftNozzleReading } from "../lib/db";
import { supabase } from "../lib/supabase";
import {
  ArrowLeft,
  Send,
  AlertTriangle,
  CheckCircle2,
  Loader2,
  WifiOff,
  RefreshCw,
} from "lucide-react";

interface SubmitProps {
  onBack: () => void;
}

interface NozzleRow extends DraftNozzleReading {
  rowId: number;
  isOpeningReadOnly?: boolean;
}



interface KhandharePetroleumRow {
  name: string;
  vehicleNumber?: string;
  slipNumber: string;
  amount: number;
}

interface DsmQrPaymentRow {
  targetDsmName: string;
  amount: number;
  tid?: string;
  batch?: string;
  slot?: string;
}

interface ExpenseRow {
  description: string;
  amount: number;
}

export default function SubmitShiftScreen({ onBack }: SubmitProps) {
  const { profile } = useAuth();
  const { syncing, saveDraft, submitToSupabase } = useSubmissionService();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const submittingRef = useRef(false);

  // Form State — pump & shift come from manager assignment, DSM cannot change them
  const pumpId = profile?.AssignedPump ?? 0;
  const shiftType = profile?.AssignedShift ?? "A";
  const [shiftDate, setShiftDate] = useState(() => {
    if (profile?.AssignedDate) {
      try {
        const d = new Date(profile.AssignedDate);
        const year = d.getFullYear();
        const month = String(d.getMonth() + 1).padStart(2, "0");
        const day = String(d.getDate()).padStart(2, "0");
        return `${year}-${month}-${day}`;
      } catch (e) {
        console.error(e);
      }
    }
    const d = new Date();
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, "0");
    const day = String(d.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
  });

  useEffect(() => {
    if (profile?.AssignedDate) {
      try {
        const d = new Date(profile.AssignedDate);
        const year = d.getFullYear();
        const month = String(d.getMonth() + 1).padStart(2, "0");
        const day = String(d.getDate()).padStart(2, "0");
        setShiftDate(`${year}-${month}-${day}`);
      } catch (e) {
        console.error(e);
      }
    }
  }, [profile]);

  const [notes, setNotes] = useState("");
  const [nozzleRows, setNozzleRows] = useState<NozzleRow[]>([]);

  // Cash Denomination states
  const [denom500, setDenom500] = useState<number>(0);
  const [denom200, setDenom200] = useState<number>(0);
  const [denom100, setDenom100] = useState<number>(0);
  const [denom50, setDenom50] = useState<number>(0);
  const [denom20, setDenom20] = useState<number>(0);
  const [denom10, setDenom10] = useState<number>(0);
  const [coins, setCoins] = useState<number>(0);
  const cash =
    denom500 * 500 +
    denom200 * 200 +
    denom100 * 100 +
    denom50 * 50 +
    denom20 * 20 +
    denom10 * 10 +
    coins;

  // Creditors & Vehicles dropdown states
  const [creditorList, setCreditorList] = useState<
    { id: string; name: string }[]
  >([]);
  const [vehicleList, setVehicleList] = useState<
    { creditorId: string; vehicleNumber: string }[]
  >([]);
  const [selectedCreditorId, setSelectedCreditorId] = useState<string>("");

  // Debtor entry adding state
  const [newDebtorName, setNewDebtorName] = useState("");
  const [newDebtorAmount, setNewDebtorAmount] = useState("");
  const [newDebtorVehicle, setNewDebtorVehicle] = useState("");
  const [newDebtorSlip, setNewDebtorSlip] = useState("");
  const [customVehicle, setCustomVehicle] = useState(false);
  const [khandhareEntries, setKhandhareEntries] = useState<KhandharePetroleumRow[]>([]);
  const [newKpName, setNewKpName] = useState("");
  const [newKpVehicleNumber, setNewKpVehicleNumber] = useState("");
  const [newKpSlipNumber, setNewKpSlipNumber] = useState("");
  const [newKpAmount, setNewKpAmount] = useState("");

  // Cross-DSM QR Payments state
  const [showCrossQrSection, setShowCrossQrSection] = useState(false);
  const [dsmUsersList, setDsmUsersList] = useState<{ id: string; fullName: string }[]>([]);
  const [qrPayments, setQrPayments] = useState<DsmQrPaymentRow[]>([]);
  const [newQrTargetDsm, setNewQrTargetDsm] = useState("");
  const [newQrAmount, setNewQrAmount] = useState("");
  const [newQrTid, setNewQrTid] = useState("");
  const [newQrBatch, setNewQrBatch] = useState("");
  // Cash 1 Deposit Amount
  const [cash1Amount, setCash1Amount] = useState<number>(0);
  const [cash1Denom500, setCash1Denom500] = useState<number>(0);
  const [cash1Denom200, setCash1Denom200] = useState<number>(0);
  const [cash1Denom100, setCash1Denom100] = useState<number>(0);
  const cashDeposit =
    cash1Amount || (cash1Denom500 * 500 + cash1Denom200 * 200 + cash1Denom100 * 100);

  const [others, setOthers] = useState(0);
  const [expenseEntries, setExpenseEntries] = useState<ExpenseRow[]>([]);
  const [newExpenseDescription, setNewExpenseDescription] = useState("");
  const [newExpenseAmount, setNewExpenseAmount] = useState("");

  const expense = expenseEntries.reduce((sum, item) => sum + item.amount, 0);
  const expenseNotes = expenseEntries
    .map((item) => `${item.description} (₹${item.amount.toFixed(2)})`)
    .join(", ");

  // Slot-based collections fields
  const [phonePeMorning, setPhonePeMorning] = useState<number>(0);
  const [phonePeTidMorning, setPhonePeTidMorning] = useState<string>("");
  const [phonePeBatchMorning, setPhonePeBatchMorning] = useState<string>("");
  const [phonePeDay, setPhonePeDay] = useState<number>(0);
  const [phonePeTidDay, setPhonePeTidDay] = useState<string>("");
  const [phonePeBatchDay, setPhonePeBatchDay] = useState<string>("");
  const [phonePeNight, setPhonePeNight] = useState<number>(0);
  const [phonePeTidNight, setPhonePeTidNight] = useState<string>("");
  const [phonePeBatchNight, setPhonePeBatchNight] = useState<string>("");

  const [creditCardMorning, setCreditCardMorning] = useState<number>(0);
  const [creditCardTidMorning, setCreditCardTidMorning] = useState<string>("");
  const [creditCardBatchMorning, setCreditCardBatchMorning] =
    useState<string>("");
  const [creditCardDay, setCreditCardDay] = useState<number>(0);
  const [creditCardTidDay, setCreditCardTidDay] = useState<string>("");
  const [creditCardBatchDay, setCreditCardBatchDay] = useState<string>("");
  const [creditCardNight, setCreditCardNight] = useState<number>(0);
  const [creditCardTidNight, setCreditCardTidNight] = useState<string>("");
  const [creditCardBatchNight, setCreditCardBatchNight] = useState<string>("");

  const [petroCardMorning, setPetroCardMorning] = useState<number>(0);
  const [petroCardTidMorning, setPetroCardTidMorning] = useState<string>("");
  const [petroCardBatchMorning, setPetroCardBatchMorning] =
    useState<string>("");
  const [petroCardDay, setPetroCardDay] = useState<number>(0);
  const [petroCardTidDay, setPetroCardTidDay] = useState<string>("");
  const [petroCardBatchDay, setPetroCardBatchDay] = useState<string>("");
  const [petroCardNight, setPetroCardNight] = useState<number>(0);
  const [petroCardTidNight, setPetroCardTidNight] = useState<string>("");
  const [petroCardBatchNight, setPetroCardBatchNight] = useState<string>("");

  const [debtorEntries, setDebtorEntries] = useState<
    {
      debtorName: string;
      amount: number;
      vehicleNumber?: string;
      slipNumber?: string;
      time: string;
    }[]
  >([]);

  const [cardSwipeDetails, setCardSwipeDetails] = useState<
    { mode: string; amount: number; tid: string; batch: string }[]
  >([]);

  // Oil & DEF Product Sales State
  const [availableProducts, setAvailableProducts] = useState<any[]>([]);
  const [productStocks, setProductStocks] = useState<Record<number, number>>(
    {},
  );
  const [salesQuantities, setSalesQuantities] = useState<
    Record<number, number>
  >({}); // productId -> quantity

  useEffect(() => {
    async function loadProductsAndStock() {
      try {
        const prods = await db.products.toArray();
        const stocks = await db.stockBalances.toArray();
        setAvailableProducts(prods);

        const stockMap: Record<number, number> = {};
        for (const s of stocks) {
          stockMap[s.productId] = s.remainingStock;
        }
        setProductStocks(stockMap);
      } catch (err) {
        console.error(
          "Failed to load products/stock from local database:",
          err,
        );
      }
    }
    loadProductsAndStock();
  }, []);

  // Loading state for nozzle config
  const [nozzleLoading, setNozzleLoading] = useState(true);
  const [nozzleError, setNozzleError] = useState("");

  // UI State
  const [step, setStep] = useState<"readings" | "collections" | "review">(
    "readings",
  );
  const [error, setError] = useState("");
  const [success, setSuccess] = useState(false);
  const [online, setOnline] = useState(navigator.onLine);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);

  const draftStorageKey = profile
    ? `dsm-shift-draft:${profile.StationId}:${profile.AssignedDate}:${profile.AssignedPump}:${profile.AssignedShift}`
    : undefined;

  useEffect(() => {
    const on = () => setOnline(true);
    const off = () => setOnline(false);
    window.addEventListener("online", on);
    window.addEventListener("offline", off);
    return () => {
      window.removeEventListener("online", on);
      window.removeEventListener("offline", off);
    };
  }, []);

  const draftRestoredRef = useRef(false);
  useEffect(() => {
    if (!profile || !draftStorageKey || draftRestoredRef.current) return;
    draftRestoredRef.current = true;
    const saved = localStorage.getItem(draftStorageKey);
    if (!saved) return;

    try {
      const parsed = JSON.parse(saved);
      if (parsed.shiftDate) setShiftDate(parsed.shiftDate);
      if (parsed.notes) setNotes(parsed.notes);
      setDenom500(parsed.denom500 ?? 0);
      setDenom200(parsed.denom200 ?? 0);
      setDenom100(parsed.denom100 ?? 0);
      setDenom50(parsed.denom50 ?? 0);
      setDenom20(parsed.denom20 ?? 0);
      setDenom10(parsed.denom10 ?? 0);
      setCoins(parsed.coins ?? 0);
      setCash1Amount(parsed.cash1Amount ?? 0);
      setCash1Denom500(parsed.cash1Denom500 ?? 0);
      setCash1Denom200(parsed.cash1Denom200 ?? 0);
      setCash1Denom100(parsed.cash1Denom100 ?? 0);

      setNewDebtorName(parsed.newDebtorName ?? "");
      setNewDebtorAmount(parsed.newDebtorAmount ?? "");
      setNewDebtorVehicle(parsed.newDebtorVehicle ?? "");
      setNewDebtorSlip(parsed.newDebtorSlip ?? "");
      setCustomVehicle(parsed.customVehicle ?? false);
      setOthers(parsed.others ?? 0);
      if (parsed.expenseEntries && Array.isArray(parsed.expenseEntries)) {
        setExpenseEntries(parsed.expenseEntries);
      } else if (parsed.expense && parsed.expense > 0) {
        setExpenseEntries([
          { description: parsed.expenseNotes || "Expense", amount: parsed.expense },
        ]);
      } else {
        setExpenseEntries([]);
      }
      setPhonePeMorning(parsed.phonePeMorning ?? 0);
      setPhonePeTidMorning(parsed.phonePeTidMorning ?? "");
      setPhonePeBatchMorning(parsed.phonePeBatchMorning ?? "");
      setPhonePeDay(parsed.phonePeDay ?? 0);
      setPhonePeTidDay(parsed.phonePeTidDay ?? "");
      setPhonePeBatchDay(parsed.phonePeBatchDay ?? "");
      setPhonePeNight(parsed.phonePeNight ?? 0);
      setPhonePeTidNight(parsed.phonePeTidNight ?? "");
      setPhonePeBatchNight(parsed.phonePeBatchNight ?? "");
      setCreditCardMorning(parsed.creditCardMorning ?? 0);
      setCreditCardTidMorning(parsed.creditCardTidMorning ?? "");
      setCreditCardBatchMorning(parsed.creditCardBatchMorning ?? "");
      setCreditCardDay(parsed.creditCardDay ?? 0);
      setCreditCardTidDay(parsed.creditCardTidDay ?? "");
      setCreditCardBatchDay(parsed.creditCardBatchDay ?? "");
      setCreditCardNight(parsed.creditCardNight ?? 0);
      setCreditCardTidNight(parsed.creditCardTidNight ?? "");
      setCreditCardBatchNight(parsed.creditCardBatchNight ?? "");
      setPetroCardMorning(parsed.petroCardMorning ?? 0);
      setPetroCardTidMorning(parsed.petroCardTidMorning ?? "");
      setPetroCardBatchMorning(parsed.petroCardBatchMorning ?? "");
      setPetroCardDay(parsed.petroCardDay ?? 0);
      setPetroCardTidDay(parsed.petroCardTidDay ?? "");
      setPetroCardBatchDay(parsed.petroCardBatchDay ?? "");
      setPetroCardNight(parsed.petroCardNight ?? 0);
      setPetroCardTidNight(parsed.petroCardTidNight ?? "");
      setPetroCardBatchNight(parsed.petroCardBatchNight ?? "");
      setDebtorEntries(parsed.debtorEntries ?? []);
      setCardSwipeDetails(parsed.cardSwipeDetails ?? []);
      setSalesQuantities(parsed.salesQuantities ?? {});
      setKhandhareEntries(parsed.khandhareEntries ?? []);
      setQrPayments(parsed.qrPayments ?? []);
      if (parsed.qrPayments && parsed.qrPayments.length > 0) {
        setShowCrossQrSection(true);
      }
      setStep(parsed.step ?? "readings");
      if (parsed.nozzleRows && Array.isArray(parsed.nozzleRows)) {
        const uniqueNozzleRows: any[] = [];
        const seenIds = new Set<number>();
        parsed.nozzleRows.forEach((row: any) => {
          if (row.nozzleId && !seenIds.has(row.nozzleId)) {
            seenIds.add(row.nozzleId);
            uniqueNozzleRows.push({
              ...row,
              rowId: uniqueNozzleRows.length + 1,
            });
          }
        });
        setNozzleRows(uniqueNozzleRows);
      }
    } catch (err) {
      console.error("Failed to restore draft:", err);
    }
  }, [profile, draftStorageKey]);

  useEffect(() => {
    if (!profile || !draftStorageKey) return;

    const draftState = {
      shiftDate,
      notes,
      denom500,
      denom200,
      denom100,
      denom50,
      denom20,
      denom10,
      coins,
      cash1Amount,
      cash1Denom500,
      cash1Denom200,
      cash1Denom100,

      newDebtorName,
      newDebtorAmount,
      newDebtorVehicle,
      newDebtorSlip,
      customVehicle,
      others,
      expenseEntries,
      newExpenseDescription,
      newExpenseAmount,
      expense,
      expenseNotes,
      phonePeMorning,
      phonePeTidMorning,
      phonePeBatchMorning,
      phonePeDay,
      phonePeTidDay,
      phonePeBatchDay,
      phonePeNight,
      phonePeTidNight,
      phonePeBatchNight,
      creditCardMorning,
      creditCardTidMorning,
      creditCardBatchMorning,
      creditCardDay,
      creditCardTidDay,
      creditCardBatchDay,
      creditCardNight,
      creditCardTidNight,
      creditCardBatchNight,
      petroCardMorning,
      petroCardTidMorning,
      petroCardBatchMorning,
      petroCardDay,
      petroCardTidDay,
      petroCardBatchDay,
      petroCardNight,
      petroCardTidNight,
      petroCardBatchNight,
      debtorEntries,
      cardSwipeDetails,
      salesQuantities,
      khandhareEntries,
      qrPayments,
      step,
      nozzleRows,
    };

    try {
      localStorage.setItem(draftStorageKey, JSON.stringify(draftState));
    } catch (err) {
      console.error("Failed to persist draft:", err);
    }
  }, [
    draftStorageKey,
    profile,
    shiftDate,
    notes,
    denom500,
    denom200,
    denom100,
    denom50,
    denom20,
    denom10,
    coins,
    cash1Denom500,
    cash1Denom200,
    cash1Denom100,

    newDebtorName,
    newDebtorAmount,
    newDebtorVehicle,
    newDebtorSlip,
    customVehicle,
    others,
    expense,
    expenseNotes,
    phonePeMorning,
    phonePeTidMorning,
    phonePeBatchMorning,
    phonePeDay,
    phonePeTidDay,
    phonePeBatchDay,
    phonePeNight,
    phonePeTidNight,
    phonePeBatchNight,
    creditCardMorning,
    creditCardTidMorning,
    creditCardBatchMorning,
    creditCardDay,
    creditCardTidDay,
    creditCardBatchDay,
    creditCardNight,
    creditCardTidNight,
    creditCardBatchNight,
    petroCardMorning,
    petroCardTidMorning,
    petroCardBatchMorning,
    petroCardDay,
    petroCardTidDay,
    petroCardBatchDay,
    petroCardNight,
    petroCardTidNight,
    petroCardBatchNight,
    debtorEntries,
    cardSwipeDetails,
    salesQuantities,
    khandhareEntries,
    step,
    nozzleRows,
  ]);

  useEffect(() => {
    if (success && profile && draftStorageKey) {
      localStorage.removeItem(draftStorageKey);
    }
  }, [success, profile, draftStorageKey]);

  // Load Creditors & Vehicles
  useEffect(() => {
    async function loadCreditors() {
      if (!profile) return;

      const cachedCreds = localStorage.getItem("cached_creditors");
      const cachedVehs = localStorage.getItem("cached_vehicles");
      if (cachedCreds) {
        setCreditorList(JSON.parse(cachedCreds));
      }
      if (cachedVehs) {
        setVehicleList(JSON.parse(cachedVehs));
      }

      if (!navigator.onLine) return;

      try {
        const { data: creds, error: credsErr } = await supabase
          .from("Creditors")
          .select("SyncGuid, Name")
          .eq("station_id", profile.StationId)
          .eq("IsActive", true)
          .order("Name", { ascending: true });

        if (!credsErr && creds) {
          const formattedCreds = creds.map((c) => ({
            id: c.SyncGuid,
            name: c.Name,
          }));
          setCreditorList(formattedCreds);
          localStorage.setItem(
            "cached_creditors",
            JSON.stringify(formattedCreds),
          );
        }

        const { data: vehs, error: vehsErr } = await supabase
          .from("DebtorVehicles")
          .select("CreditorId, VehicleNumber")
          .eq("station_id", profile.StationId)
          .eq("IsActive", true);

        if (!vehsErr && vehs) {
          const formattedVehs = vehs.map((v) => ({
            creditorId: v.CreditorId,
            vehicleNumber: v.VehicleNumber,
          }));
          setVehicleList(formattedVehs);
          localStorage.setItem(
            "cached_vehicles",
            JSON.stringify(formattedVehs),
          );
        }

        // Load DSM Users for Cross-DSM QR dropdown strictly for THIS station
        let formattedUsers: { id: string; fullName: string }[] = [];
        const stationId = (profile.StationId || "").trim();
        const cacheKey = `cached_dsm_users_${stationId}`;

        if (stationId) {
          try {
            // 1. Fetch from DsmUsers matching THIS station ID
            const { data: dsmUsers, error: dsmErr } = await supabase
              .from("DsmUsers")
              .select("SyncGuid, FullName, station_id, IsActive")
              .eq("station_id", stationId)
              .eq("IsActive", true)
              .order("FullName");

            if (!dsmErr && dsmUsers && dsmUsers.length > 0) {
              const userMap = new Map<string, string>();
              for (const u of dsmUsers) {
                if (u.FullName && u.FullName.trim()) {
                  userMap.set(u.FullName.trim(), u.SyncGuid || u.FullName.trim());
                }
              }
              formattedUsers = Array.from(userMap.entries()).map(([fullName, id]) => ({
                id,
                fullName,
              }));
            }
          } catch (err) {
            console.warn("Could not load from DsmUsers table:", err);
          }

          // 2. Fallback: Check DsmProfiles strictly for THIS station ID
          if (formattedUsers.length === 0) {
            try {
              const { data: profiles } = await supabase
                .from("DsmProfiles")
                .select("DsmName, station_id")
                .eq("station_id", stationId)
                .order("DsmName");

              if (profiles && profiles.length > 0) {
                const nameSet = new Set<string>();
                for (const p of profiles) {
                  if (p.DsmName && p.DsmName.trim()) {
                    nameSet.add(p.DsmName.trim());
                  }
                }
                formattedUsers = Array.from(nameSet).map((name) => ({
                  id: name,
                  fullName: name,
                }));
              }
            } catch (profErr) {
              console.warn("Could not load from DsmProfiles:", profErr);
            }
          }

          // 3. Fallback: Check DsmPumpAssignments for active DSMs at THIS station
          if (formattedUsers.length === 0) {
            try {
              const { data: assignments } = await supabase
                .from("DsmPumpAssignments")
                .select("DsmUsers(FullName, SyncGuid), station_id")
                .eq("station_id", stationId);

              if (assignments && assignments.length > 0) {
                const nameSet = new Map<string, string>();
                for (const a of assignments as any[]) {
                  const name = a.DsmUsers?.FullName;
                  const guid = a.DsmUsers?.SyncGuid || name;
                  if (name && name.trim()) {
                    nameSet.set(name.trim(), guid);
                  }
                }
                formattedUsers = Array.from(nameSet.entries()).map(([fullName, id]) => ({
                  id,
                  fullName,
                }));
              }
            } catch (assignErr) {
              console.warn("Could not load from DsmPumpAssignments:", assignErr);
            }
          }
        }

        if (formattedUsers.length > 0) {
          formattedUsers.sort((a, b) => a.fullName.localeCompare(b.fullName));
          setDsmUsersList(formattedUsers);
          localStorage.setItem(cacheKey, JSON.stringify(formattedUsers));
        } else {
          const cached = localStorage.getItem(cacheKey);
          if (cached) {
            try {
              setDsmUsersList(JSON.parse(cached));
            } catch {}
          }
        }
      } catch (e) {
        console.error("Failed to load creditors/vehicles/DSM users from Supabase:", e);
      }
    }
    loadCreditors();
  }, [profile]);



  // ── Load nozzle config from Supabase (set by manager) ───────
  const lastLoadedPumpRef = useRef<number | null>(null);
  async function loadNozzleConfig() {
    if (!profile || !pumpId) return;

    setNozzleLoading(true);
    setNozzleError("");

    try {
      const station =
        profile?.StationId ||
        localStorage.getItem("current_station_id") ||
        localStorage.getItem("fuelpro_station_id") ||
        "";

      // 1. Fetch rates from Settings
      let hsdRate = 90.35;
      let msIRate = 103.81;
      let msIIRate = 103.81;
      let cngRate = 85.0;

      // Fast check cached rates first
      try {
        const ratesCacheKey = `nozzle_rates_${station || "default"}`;
        const cachedRates = localStorage.getItem(ratesCacheKey);
        if (cachedRates) {
          const parsed = JSON.parse(cachedRates);
          if (parsed.hsd) hsdRate = parsed.hsd;
          if (parsed.msI) msIRate = parsed.msI;
          if (parsed.msII) msIIRate = parsed.msII;
          if (parsed.cng) cngRate = parsed.cng;
        }
      } catch {}

      try {
        let settingsQuery = supabase
          .from("Settings")
          .select("HsdRate, MsIRate, MsIIRate, CngRate, LastUpdated");

        if (station) {
          settingsQuery = settingsQuery.or(`station_id.eq.${station},station_id.is.null`);
        }

        const { data: settingsData } = await settingsQuery
          .order("LastUpdated", { ascending: false, nullsFirst: false })
          .limit(1);

        if (settingsData && settingsData.length > 0) {
          if (settingsData[0].HsdRate) hsdRate = Number(settingsData[0].HsdRate);
          if (settingsData[0].MsIRate) msIRate = Number(settingsData[0].MsIRate);
          if (settingsData[0].MsIIRate) msIIRate = Number(settingsData[0].MsIIRate);
          if (settingsData[0].CngRate) cngRate = Number(settingsData[0].CngRate);
        }
      } catch (e) {
        console.error("Failed to fetch rates from Settings:", e);
      }

      // We load nozzles of both the primary and connected pump (deduplicated)
      const pumpsToFetch = Array.from(
        new Set(
          [pumpId, profile.ConnectedPump].filter(
            (p): p is number => typeof p === "number" && p > 0,
          ),
        ),
      );

      // Try to fetch nozzle config from Supabase PumpMappings table
      let nozzleConfig: any[] | null = null;
      let nozzleErr: any = null;
      try {
        let pmQuery = supabase
          .from("PumpMappings")
          .select("NozzleNumber, FuelType, PumpId")
          .in("PumpId", pumpsToFetch)
          .eq("IsActive", true);

        if (station) {
          pmQuery = pmQuery.or(`station_id.eq.${station},station_id.is.null`);
        }

        const pmRes = await pmQuery.order("NozzleNumber", { ascending: true });
        nozzleConfig = pmRes.data;
        nozzleErr = pmRes.error;
      } catch (pmEx) {
        console.error("Error querying PumpMappings:", pmEx);
      }

      let configRows: { nozzleId: number; fuelType: string; pumpId: number }[] =
        [];

      if (!nozzleErr && nozzleConfig && nozzleConfig.length > 0) {
        // Use Supabase config - deduplicate by NozzleNumber to prevent duplicate nozzle input cards
        const seenNozzles = new Set<number>();
        for (const r of nozzleConfig) {
          const nId = Number(r.NozzleNumber);
          if (nId && !seenNozzles.has(nId)) {
            seenNozzles.add(nId);
            configRows.push({
              nozzleId: nId,
              fuelType: r.FuelType,
              pumpId: r.PumpId,
            });
          }
        }
      } else {
        const FALLBACK_CONFIG: Record<
          number,
          { nozzleId: number; fuelType: string; pumpId: number }[]
        > = {
          1: [
            { nozzleId: 1, fuelType: "MS-I", pumpId: 1 },
            { nozzleId: 3, fuelType: "HSD", pumpId: 1 },
          ],
          2: [
            { nozzleId: 2, fuelType: "MS-I", pumpId: 2 },
            { nozzleId: 4, fuelType: "HSD", pumpId: 2 },
          ],
          3: [
            { nozzleId: 5, fuelType: "MS-I", pumpId: 3 },
            { nozzleId: 7, fuelType: "HSD", pumpId: 3 },
          ],
          4: [
            { nozzleId: 6, fuelType: "MS-I", pumpId: 4 },
            { nozzleId: 8, fuelType: "HSD", pumpId: 4 },
          ],
          5: [
            { nozzleId: 9, fuelType: "MS-I", pumpId: 5 },
            { nozzleId: 11, fuelType: "HSD", pumpId: 5 },
          ],
          6: [
            { nozzleId: 10, fuelType: "MS-I", pumpId: 6 },
            { nozzleId: 12, fuelType: "HSD", pumpId: 6 },
          ],
        };

        const uniqueMap = new Map<
          number,
          { nozzleId: number; fuelType: string; pumpId: number }
        >();
        pumpsToFetch.forEach((pId) => {
          if (FALLBACK_CONFIG[pId]) {
            FALLBACK_CONFIG[pId].forEach((item) => {
              if (!uniqueMap.has(item.nozzleId)) {
                uniqueMap.set(item.nozzleId, item);
              }
            });
          }
        });
        configRows = Array.from(uniqueMap.values());
      }

      if (configRows.length === 0) {
        setNozzleError(
          `No nozzle configuration found for Pump ${pumpId}. Please contact your manager.`,
        );
        setNozzleRows([]);
        return;
      }

      const prevClosings: Record<number, number> = {};
      const nozzleRates: Record<number, number> = {};
      const nozzleFuelTypes: Record<number, string> = {};
      const nozzleIds = configRows.map((r) => r.nozzleId);

      // Fast check cached openings from localStorage
      const openingsCacheKey = `nozzle_openings_${station || "default"}`;
      try {
        const cachedOpeningsStr = localStorage.getItem(openingsCacheKey);
        if (cachedOpeningsStr) {
          const parsed = JSON.parse(cachedOpeningsStr);
          nozzleIds.forEach((nId) => {
            if (parsed[nId] !== undefined && Number(parsed[nId]) > 0) {
              prevClosings[nId] = Number(parsed[nId]);
            }
          });
        }
      } catch {}

      // ── Step 1: Most recent closing reading from NozzleReadings (Admin Side Ground Truth) ─
      // Query specifically per nozzle ordered by ClosingReading descending across all station records.
      // Since pump fuel meters strictly increase with each shift, the highest recorded closing reading
      // represents the latest authoritative ground truth from the admin side.
      try {
        await Promise.all(
          nozzleIds.map(async (nId) => {
            try {
              let readings: any[] | null = null;
              if (station) {
                const { data } = await supabase
                  .from("NozzleReadings")
                  .select("NozzleNumber, ClosingReading, OpeningReading, Rate, FuelType, NozzleReadingId, created_at, station_id")
                  .eq("NozzleNumber", nId)
                  .gt("ClosingReading", 0)
                  .or(`station_id.eq.${station},station_id.eq.KANDHARE-PETROLEUM,station_id.is.null`)
                  .order("ClosingReading", { ascending: false, nullsFirst: false })
                  .limit(5);
                readings = data;
              }

              // Fallback: If no records match exact station_id, query latest across all entries for this nozzle
              if (!readings || readings.length === 0) {
                const { data } = await supabase
                  .from("NozzleReadings")
                  .select("NozzleNumber, ClosingReading, OpeningReading, Rate, FuelType, NozzleReadingId, created_at, station_id")
                  .eq("NozzleNumber", nId)
                  .gt("ClosingReading", 0)
                  .order("ClosingReading", { ascending: false, nullsFirst: false })
                  .limit(5);
                readings = data;
              }

              if (readings && readings.length > 0) {
                const bestEntry = readings.reduce((prev, curr) =>
                  Number(curr.ClosingReading) > Number(prev.ClosingReading) ? curr : prev
                );
                const val = Number(bestEntry.ClosingReading);
                if (val > 0) {
                  prevClosings[nId] = Math.max(val, prevClosings[nId] || 0);
                  if (bestEntry.Rate) nozzleRates[nId] = Number(bestEntry.Rate);
                  if (bestEntry.FuelType) nozzleFuelTypes[nId] = bestEntry.FuelType;
                }
              }
            } catch (err) {
              console.error(`Failed to fetch latest NozzleReading for nozzle ${nId}:`, err);
            }
          })
        );
      } catch (e) {
        console.error("Failed to fetch latest NozzleReadings from admin side:", e);
      }

      // ── Step 2: DsmSubmissions for nozzles not yet in NozzleReadings or newer PWA submissions ───────────────
      try {
        if (station) {
          const { data: lastSubmissions } = await supabase
            .from("DsmSubmissions")
            .select("Id, ShiftDate, ShiftType, PumpId, SubmittedAt")
            .or(`StationId.eq.${station},StationId.is.null`)
            .in("PumpId", pumpsToFetch)
            .in("Status", ["Approved", "Pending"])
            .order("ShiftDate", { ascending: false })
            .order("SubmittedAt", { ascending: false })
            .limit(10);

          if (lastSubmissions && lastSubmissions.length > 0) {
            for (const sub of lastSubmissions) {
              const { data: lastReadings } = await supabase
                .from("DsmSubmissionReadings")
                .select("NozzleId, ClosingReading, Rate, FuelType")
                .eq("SubmissionId", sub.Id);

              if (lastReadings && lastReadings.length > 0) {
                lastReadings.forEach((r: any) => {
                  const nId = Number(r.NozzleId);
                  const cVal = Number(r.ClosingReading);
                  if (cVal > 0) {
                    if (prevClosings[nId] === undefined || cVal > prevClosings[nId]) {
                      prevClosings[nId] = cVal;
                    }
                    if (r.Rate && !nozzleRates[nId]) {
                      nozzleRates[nId] = Number(r.Rate);
                    }
                  }
                });
              }
            }
          }
        }
      } catch (e) {
        console.error("Failed to fetch from DsmSubmissions:", e);
      }

      // ── Step 3: Local IndexedDB drafts (offline fallback) ─────────────────────────────────
      try {
        const missingAfterSteps12 = nozzleIds.filter(
          (nId) => prevClosings[nId] === undefined,
        );
        if (missingAfterSteps12.length > 0) {
          let localDrafts: any[] = [];
          try {
            localDrafts = await db.drafts
              .filter((d: any) => pumpsToFetch.includes(d.pumpId))
              .toArray();
          } catch {
            localDrafts = await db.drafts.toArray();
          }

          if (localDrafts && localDrafts.length > 0) {
            const sortedDrafts = localDrafts
              .filter((d: any) => pumpsToFetch.includes(d.pumpId))
              .sort(
                (a, b) =>
                  (b.shiftDate || "").localeCompare(a.shiftDate || "") ||
                  (b.createdAt || "").localeCompare(a.createdAt || ""),
              );
            const mostRecentDraft = sortedDrafts[0];
            if (mostRecentDraft?.nozzleReadings) {
              mostRecentDraft.nozzleReadings.forEach((nr: any) => {
                if (nr.closingReading > 0 && prevClosings[nr.nozzleId] === undefined) {
                  prevClosings[nr.nozzleId] = Number(nr.closingReading);
                }
              });
            }
          }
        }
      } catch (e) {
        console.warn("Failed to fetch from IndexedDB drafts:", e);
      }

      // Save verified openings and rates to localStorage cache for offline persistence
      try {
        localStorage.setItem(openingsCacheKey, JSON.stringify(prevClosings));
        const ratesCacheKey = `nozzle_rates_${station || "default"}`;
        localStorage.setItem(
          ratesCacheKey,
          JSON.stringify({ hsd: hsdRate, msI: msIRate, msII: msIIRate, cng: cngRate, perNozzle: nozzleRates })
        );
      } catch {}

      // Auto-update general fuel rates if retrieved from recent entries
      for (const nId of nozzleIds) {
        const rRate = nozzleRates[nId];
        const rType = (nozzleFuelTypes[nId] || "").toUpperCase();
        if (rRate && rRate > 0) {
          if (rType.startsWith("HSD") || rType.includes("DIESEL")) {
            hsdRate = rRate;
          } else if (rType.startsWith("MS-II") || rType.startsWith("MS_II")) {
            msIIRate = rRate;
          } else if (rType.startsWith("MS") || rType.includes("PETROL")) {
            msIRate = rRate;
          }
        }
      }

      // Read existing draft readings from localStorage to avoid overwriting typed values on refresh or app minimize
      const draftMap: Record<number, { closingReading: number; testing: number; openingReading?: number }> = {};
      if (draftStorageKey) {
        try {
          const savedDraft = localStorage.getItem(draftStorageKey);
          if (savedDraft) {
            const parsed = JSON.parse(savedDraft);
            if (parsed.nozzleRows && Array.isArray(parsed.nozzleRows)) {
              parsed.nozzleRows.forEach((r: any) => {
                if (r.nozzleId) {
                  draftMap[r.nozzleId] = {
                    closingReading: Number(r.closingReading) || 0,
                    testing: Number(r.testing) || 0,
                    openingReading: Number(r.openingReading) || 0,
                  };
                }
              });
            }
          }
        } catch (e) {
          console.error("Failed to read draft map for nozzle rows:", e);
        }
      }

      // Build updated nozzle rows while strictly PRESERVING user-typed closingReading & testing values
      setNozzleRows((currentRows) => {
        const currentMap = new Map<number, NozzleRow>();
        currentRows.forEach((r) => currentMap.set(r.nozzleId, r));

        return configRows.map((n, index) => {
          let rate = msIRate;
          const effectiveFuelType = (nozzleFuelTypes[n.nozzleId] || n.fuelType || "").trim().toUpperCase();
          if (effectiveFuelType) {
            if (effectiveFuelType.startsWith("HSD") || effectiveFuelType.includes("DIESEL")) {
              rate = hsdRate;
            } else if (effectiveFuelType.startsWith("MS-II") || effectiveFuelType.startsWith("MS_II") || effectiveFuelType.startsWith("MS2")) {
              rate = msIIRate;
            } else if (effectiveFuelType.startsWith("MS") || effectiveFuelType.includes("PETROL")) {
              rate = msIRate;
            } else if (effectiveFuelType.startsWith("CNG")) {
              rate = cngRate;
            }
          }
          if (nozzleRates[n.nozzleId] && nozzleRates[n.nozzleId] > 0) {
            rate = nozzleRates[n.nozzleId];
          }

          const prevClosing = prevClosings[n.nozzleId] || 0;
          const existingRow = currentMap.get(n.nozzleId);
          const draftEntry = draftMap[n.nozzleId];

          // Opening reading: authoritative closing from previous shift, else existing state, else draft
          const opening = prevClosing > 0
            ? prevClosing
            : (existingRow && existingRow.openingReading ? existingRow.openingReading : (draftEntry?.openingReading || 0));

          // Closing reading: ALWAYS preserve whatever the user actively typed in memory, else restore draft
          let closing = 0;
          if (existingRow && existingRow.closingReading > 0) {
            closing = existingRow.closingReading;
          } else if (draftEntry && draftEntry.closingReading && draftEntry.closingReading >= opening) {
            closing = draftEntry.closingReading;
          }

          // Testing: preserve existing user input or draft
          let testing = 0;
          if (existingRow && existingRow.testing !== undefined && existingRow.testing > 0) {
            testing = existingRow.testing;
          } else if (draftEntry?.testing) {
            testing = draftEntry.testing;
          }

          return {
            rowId: index + 1,
            nozzleId: n.nozzleId,
            fuelType: n.fuelType,
            openingReading: opening,
            closingReading: closing,
            rate,
            isOpeningReadOnly: prevClosing > 0,
            pumpId: n.pumpId,
            testing,
          };
        });
      });
    } catch (err) {
      console.error("Failed to load nozzle config:", err);
      setNozzleError(
        "Failed to load nozzle configuration. Please check your connection and try again.",
      );
    } finally {
      setNozzleLoading(false);
    }
  }

  useEffect(() => {
    if (profile && pumpId > 0 && lastLoadedPumpRef.current !== pumpId) {
      lastLoadedPumpRef.current = pumpId;
      loadNozzleConfig();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pumpId, profile?.id]);

  function updateNozzle(
    rowId: number,
    field: keyof DraftNozzleReading,
    value: string | number,
  ) {
    setNozzleRows((prev) =>
      prev.map((r) =>
        r.rowId === rowId
          ? { ...r, [field]: typeof value === "string" ? value : Number(value) }
          : r,
      ),
    );
  }

  // ── Computed totals ──────────────────────────────────────────
  const grossSales = nozzleRows.reduce(
    (sum, r) => sum + Math.max(0, r.closingReading - r.openingReading) * r.rate,
    0,
  );
  const upiTotal =
    shiftType === "B" ? phonePeDay : phonePeMorning + phonePeNight;
  const cardTotal =
    shiftType === "B" ? creditCardDay : creditCardMorning + creditCardNight;
  const petroCardTotal =
    shiftType === "B" ? petroCardDay : petroCardMorning + petroCardNight;
  const creditTotal = debtorEntries.reduce((sum, d) => sum + d.amount, 0);
  const totalTesting = nozzleRows.reduce(
    (sum, r) => sum + (r.testing || 0) * r.rate,
    0,
  );

  const shiftOilTotal = availableProducts
    .filter((p) => p.category === "Oil")
    .reduce(
      (sum, p) => sum + (salesQuantities[p.id] || 0) * p.defaultSaleRate,
      0,
    );

  const shiftDefTotal = availableProducts
    .filter((p) => p.category === "DEF")
    .reduce(
      (sum, p) => sum + (salesQuantities[p.id] || 0) * p.defaultSaleRate,
      0,
    );

  const grandProductSales = shiftOilTotal + shiftDefTotal;

  const kpTotal = khandhareEntries.reduce((sum, item) => sum + item.amount, 0);
  const qrTotal = qrPayments.reduce((sum, item) => sum + item.amount, 0);
  const totalCollections =
    cash +
    upiTotal +
    cardTotal +
    petroCardTotal +
    cashDeposit +
    creditTotal +
    totalTesting +
    kpTotal +
    qrTotal;
  const mismatch =
    totalCollections + expense - (grossSales + grandProductSales);

  // ── Validation ───────────────────────────────────────────────
  function validateReadings(): string[] {
    const errs: string[] = [];
    if (nozzleRows.length === 0) {
      errs.push(
        "No nozzle readings loaded. Please reload the page or contact your manager.",
      );
      return errs;
    }
    nozzleRows.forEach((r) => {
      const salesLtrs = r.closingReading - r.openingReading;
      if (r.closingReading < r.openingReading)
        errs.push(
          `Nozzle ${r.nozzleId} (${r.fuelType}): Closing (${r.closingReading}) < Opening (${r.openingReading})`,
        );
      if (r.testing && r.testing > salesLtrs)
        errs.push(
          `Nozzle ${r.nozzleId} (${r.fuelType}): Testing (${r.testing} Ltr) cannot exceed dispensed fuel (${salesLtrs.toFixed(2)} Ltr)`,
        );
      if (r.openingReading < 0 || r.closingReading < 0 || (r.testing || 0) < 0)
        errs.push(`Nozzle ${r.nozzleId}: Negative values are not allowed`);
      if (r.rate <= 0) errs.push(`Nozzle ${r.nozzleId}: Rate must be positive`);
    });
    return errs;
  }

  function validateCollections(): string[] {
    const errs: string[] = [];
    if (totalCollections < 0) errs.push("Total collections cannot be negative");
    if (Math.abs(mismatch) > 10000)
      errs.push(
        `Mismatch of ₹${mismatch.toFixed(2)} is unusually high. Please verify readings.`,
      );

    const checkSlot = (
      label: string,
      amt: number,
      tid: string,
      batch: string,
    ) => {
      if (amt > 0) {
        if (!tid || !tid.trim()) {
          errs.push(`${label}: TID is required when amount is greater than 0.`);
        }
        if (!batch || !batch.trim()) {
          errs.push(
            `${label}: Batch number is required when amount is greater than 0.`,
          );
        }
      }
    };

    if (shiftType === "B") {
      checkSlot("PhonePe (Day)", phonePeDay, phonePeTidDay, phonePeBatchDay);
      checkSlot(
        "PineLabs Credit Card (Day)",
        creditCardDay,
        creditCardTidDay,
        creditCardBatchDay,
      );
      checkSlot(
        "Petro Card (Day)",
        petroCardDay,
        petroCardTidDay,
        petroCardBatchDay,
      );
    } else {
      checkSlot(
        "PhonePe (Morning)",
        phonePeMorning,
        phonePeTidMorning,
        phonePeBatchMorning,
      );
      checkSlot(
        "PhonePe (Night)",
        phonePeNight,
        phonePeTidNight,
        phonePeBatchNight,
      );
      checkSlot(
        "PineLabs Credit Card (Morning)",
        creditCardMorning,
        creditCardTidMorning,
        creditCardBatchMorning,
      );
      checkSlot(
        "PineLabs Credit Card (Night)",
        creditCardNight,
        creditCardTidNight,
        creditCardBatchNight,
      );
      checkSlot(
        "Petro Card (Morning)",
        petroCardMorning,
        petroCardTidMorning,
        petroCardBatchMorning,
      );
      checkSlot(
        "Petro Card (Night)",
        petroCardNight,
        petroCardTidNight,
        petroCardBatchNight,
      );
    }

    return errs;
  }



  // ── Submit ───────────────────────────────────────────────────
  async function handleSubmit() {
    if (!profile) return;
    if (submittingRef.current || isSubmitting || syncing) return;

    setError("");

    const readingErrs = validateReadings();
    const collectionErrs = validateCollections();
    const allErrs = [...readingErrs, ...collectionErrs];
    if (allErrs.length > 0) {
      setValidationErrors(allErrs);
      const errBox = document.getElementById("validation-errors-box");
      if (errBox) {
        errBox.scrollIntoView({ behavior: "smooth" });
      } else {
        window.scrollTo({ top: 0, behavior: "smooth" });
      }
      return;
    }
    setValidationErrors([]);

    submittingRef.current = true;
    setIsSubmitting(true);

    const cardSwipes = [];
    const settlementsList = [];
    if (shiftType === "B") {
      if (phonePeDay > 0) {
        cardSwipes.push({
          mode: "PhonePe Day",
          amount: phonePeDay,
          tid: phonePeTidDay,
          batch: phonePeBatchDay,
        });
        settlementsList.push({
          paymentType: "PhonePe",
          period: "Day",
          amount: phonePeDay,
          tid: phonePeTidDay,
          batch: phonePeBatchDay,
          businessDate: shiftDate,
          operationalShift: "B",
        });
      }
      if (creditCardDay > 0) {
        cardSwipes.push({
          mode: "PineLabs Card Day",
          amount: creditCardDay,
          tid: creditCardTidDay,
          batch: creditCardBatchDay,
        });
        settlementsList.push({
          paymentType: "PineLabs",
          period: "Day",
          amount: creditCardDay,
          tid: creditCardTidDay,
          batch: creditCardBatchDay,
          businessDate: shiftDate,
          operationalShift: "B",
        });
      }
      if (petroCardDay > 0) {
        cardSwipes.push({
          mode: "PetroCard Day",
          amount: petroCardDay,
          tid: petroCardTidDay,
          batch: petroCardBatchDay,
        });
        settlementsList.push({
          paymentType: "PetroCard",
          period: "Day",
          amount: petroCardDay,
          tid: petroCardTidDay,
          batch: petroCardBatchDay,
          businessDate: shiftDate,
          operationalShift: "B",
        });
      }
    } else {
      if (phonePeMorning > 0) {
        cardSwipes.push({
          mode: "PhonePe Morning",
          amount: phonePeMorning,
          tid: phonePeTidMorning,
          batch: phonePeBatchMorning,
        });
        settlementsList.push({
          paymentType: "PhonePe",
          period: "Morning",
          amount: phonePeMorning,
          tid: phonePeTidMorning,
          batch: phonePeBatchMorning,
          businessDate: shiftDate,
          operationalShift: "A",
        });
      }
      if (phonePeNight > 0) {
        cardSwipes.push({
          mode: "PhonePe Night",
          amount: phonePeNight,
          tid: phonePeTidNight,
          batch: phonePeBatchNight,
        });
        settlementsList.push({
          paymentType: "PhonePe",
          period: "Night",
          amount: phonePeNight,
          tid: phonePeTidNight,
          batch: phonePeBatchNight,
          businessDate: shiftDate,
          operationalShift: "A",
        });
      }
      if (creditCardMorning > 0) {
        cardSwipes.push({
          mode: "PineLabs Card Morning",
          amount: creditCardMorning,
          tid: creditCardTidMorning,
          batch: creditCardBatchMorning,
        });
        settlementsList.push({
          paymentType: "PineLabs",
          period: "Morning",
          amount: creditCardMorning,
          tid: creditCardTidMorning,
          batch: creditCardBatchMorning,
          businessDate: shiftDate,
          operationalShift: "A",
        });
      }
      if (creditCardNight > 0) {
        cardSwipes.push({
          mode: "PineLabs Card Night",
          amount: creditCardNight,
          tid: creditCardTidNight,
          batch: creditCardBatchNight,
        });
        settlementsList.push({
          paymentType: "PineLabs",
          period: "Night",
          amount: creditCardNight,
          tid: creditCardTidNight,
          batch: creditCardBatchNight,
          businessDate: shiftDate,
          operationalShift: "A",
        });
      }
      if (petroCardMorning > 0) {
        cardSwipes.push({
          mode: "PetroCard Morning",
          amount: petroCardMorning,
          tid: petroCardTidMorning,
          batch: petroCardBatchMorning,
        });
        settlementsList.push({
          paymentType: "PetroCard",
          period: "Morning",
          amount: petroCardMorning,
          tid: petroCardTidMorning,
          batch: petroCardBatchMorning,
          businessDate: shiftDate,
          operationalShift: "A",
        });
      }
      if (petroCardNight > 0) {
        cardSwipes.push({
          mode: "PetroCard Night",
          amount: petroCardNight,
          tid: petroCardTidNight,
          batch: petroCardBatchNight,
        });
        settlementsList.push({
          paymentType: "PetroCard",
          period: "Night",
          amount: petroCardNight,
          tid: petroCardTidNight,
          batch: petroCardBatchNight,
          businessDate: shiftDate,
          operationalShift: "A",
        });
      }
    }

    const draftData = {
      pumpId,
      shiftDate,
      shiftType: shiftType as "A" | "B" | "C",
      notes,
      nozzleReadings: nozzleRows.map(
        ({ rowId: _r, isOpeningReadOnly, ...rest }) => rest,
      ),
      cash,
      upi: upiTotal,
      card: cardTotal,
      petroCard: petroCardTotal,
      cashDeposit,
      others,
      credit: creditTotal,
      expense,
      expenseNotes,
      expenseEntries: expenseEntries.map((e) => ({
        description: e.description,
        amount: e.amount,
      })),
      short: 0,
      excess: 0,
      cardSwipeDetails: cardSwipes,
      settlements: settlementsList,
      debtorEntries,
      personalDebtors: [],
      khandhareEntries,
      qrPayments: qrPayments.map((q) => ({
        targetDsmName: q.targetDsmName,
        amount: q.amount,
        tid: q.tid,
        batch: q.batch,
        slot: q.slot,
      })),
      cash1Amount,
      cash1Denominations: {
        denom500: cash1Denom500,
        denom200: cash1Denom200,
        denom100: cash1Denom100,
        denom50: 0,
        denom20: 0,
        denom10: 0,
        coins: 0,
      },
      cashDenominations: {
        denom500,
        denom200,
        denom100,
        denom50,
        denom20,
        denom10,
        coins,
      },
      testingEntries: nozzleRows
        .filter((r) => (r.testing || 0) > 0)
        .map((r) => ({
          nozzleId: r.nozzleId,
          fuelType: r.fuelType,
          amount: r.testing || 0,
          pumpId: r.pumpId || pumpId,
        })),
      phonePeMorning,
      phonePeTidMorning,
      phonePeBatchMorning,
      phonePeDay,
      phonePeTidDay,
      phonePeBatchDay,
      phonePeNight,
      phonePeTidNight,
      phonePeBatchNight,
      creditCardMorning,
      creditCardTidMorning,
      creditCardBatchMorning,
      creditCardDay,
      creditCardTidDay,
      creditCardBatchDay,
      creditCardNight,
      creditCardTidNight,
      creditCardBatchNight,
      petroCardMorning,
      petroCardTidMorning,
      petroCardBatchMorning,
      petroCardDay,
      petroCardTidDay,
      petroCardBatchDay,
      petroCardNight,
      petroCardTidNight,
      petroCardBatchNight,
      oilDefSales: availableProducts
        .filter((p) => (salesQuantities[p.id] || 0) > 0)
        .map((p) => ({
          productId: p.id,
          productName: p.productName,
          category: p.category,
          unit: p.unit,
          quantity: salesQuantities[p.id],
          price: p.defaultSaleRate,
          total: (salesQuantities[p.id] || 0) * p.defaultSaleRate,
        })),
    };

    try {
      if (!online) {
        const saved = await saveDraft({
          ...draftData,
          status: "queued",
        } as Parameters<typeof saveDraft>[0]);
        if (saved) setSuccess(true);
        return;
      }

      const savedDraft = await saveDraft(
        draftData as Parameters<typeof saveDraft>[0],
      );
      const err = await submitToSupabase(
        savedDraft,
        profile.id,
        profile.StationId,
      );
      if (err) {
        setError(err);
      } else {
        setSuccess(true);
      }
    } catch (e: any) {
      setError(e?.message || "An unexpected error occurred during submission.");
    } finally {
      setIsSubmitting(false);
      submittingRef.current = false;
    }
  }

  if (success) {
    return (
      <div className="screen success-screen">
        <div className="success-card">
          <CheckCircle2 size={64} className="success-icon" />
          <h1 className="success-title">
            {online ? "Submitted!" : "Saved Offline!"}
          </h1>
          <p className="success-msg">
            {online
              ? "Your shift entry has been submitted for manager approval."
              : "Your entry is saved and will be submitted when you're back online."}
          </p>
          <button
            id="back-to-dashboard-btn"
            className="btn-primary"
            onClick={onBack}
          >
            Back to Dashboard
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="screen submit-screen">
      {/* Header */}
      <header className="app-header">
        <button className="icon-btn" onClick={onBack} aria-label="Back">
          <ArrowLeft size={22} />
        </button>
        <span className="header-title">New Shift Entry</span>
        {!online && (
          <div className="offline-badge">
            <WifiOff size={16} />
            <span>Offline</span>
          </div>
        )}
      </header>

      <main className="submit-main" style={{ paddingBottom: "30px" }}>
        <div className="form-section" id="single-page-shift-entry">
          {/* ── SECTION 1: SHIFT INFO & NOZZLE READINGS ── */}
          <h2 className="section-heading">Shift Information</h2>

          {/* Pump & Shift — read-only, assigned by manager */}
          <div className="field-row-2">
            <div className="field-group">
              <label className="field-label">Assigned Pump</label>
              <div
                className="field-input"
                style={{
                  background: "#1e293b",
                  display: "flex",
                  alignItems: "center",
                  minHeight: "42px",
                  paddingLeft: "12px",
                  fontWeight: "bold",
                  color: "#f8fafc",
                  borderRadius: "0.375rem",
                }}
              >
                Pump {pumpId}
                {profile?.ConnectedPump
                  ? ` + Pump ${profile.ConnectedPump} (Connected)`
                  : ""}
              </div>
              {profile?.ConnectedPump && (
                <span
                  style={{
                    fontSize: "0.7rem",
                    color: "#38bdf8",
                    marginTop: "4px",
                    display: "block",
                    lineHeight: "1.2",
                  }}
                >
                  ℹ️ You are entering readings for both Pump {pumpId} and
                  Connected Pump {profile.ConnectedPump}.
                </span>
              )}
            </div>
            <div className="field-group">
              <label className="field-label">Assigned Shift</label>
              <div
                className="field-input"
                style={{
                  background: "#1e293b",
                  display: "flex",
                  alignItems: "center",
                  minHeight: "42px",
                  paddingLeft: "12px",
                  fontWeight: "bold",
                  color: "#f8fafc",
                  borderRadius: "0.375rem",
                }}
              >
                Shift {shiftType} ({shiftType === "A" ? "Night/Morning" : "Day"})
              </div>
            </div>
          </div>

          <div className="field-group">
            <label className="field-label">Shift Date</label>
            <div
              className="field-input"
              style={{
                background: "#1e293b",
                display: "flex",
                alignItems: "center",
                minHeight: "42px",
                paddingLeft: "12px",
                fontWeight: "bold",
                color: "#f8fafc",
                borderRadius: "0.375rem",
              }}
            >
              {new Date(shiftDate + "T00:00:00").toLocaleDateString("en-IN", {
                day: "numeric",
                month: "long",
                year: "numeric",
              })}
            </div>
          </div>

          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginTop: "1.5rem",
              marginBottom: "0.5rem",
            }}
          >
            <h2 className="section-heading" style={{ margin: 0 }}>
              Nozzle Readings
            </h2>
            <button
              type="button"
              className="btn-outline"
              style={{
                padding: "4px 10px",
                fontSize: "0.75rem",
                display: "flex",
                alignItems: "center",
                gap: "6px",
                borderColor: "#3b82f6",
                color: "#60a5fa",
              }}
              onClick={loadNozzleConfig}
              disabled={nozzleLoading}
            >
              <RefreshCw size={13} className={nozzleLoading ? "spin" : ""} />
              {nozzleLoading ? "Refreshing..." : "Refresh Readings"}
            </button>
          </div>

          {/* Loading / Error states for nozzle config */}
          {nozzleLoading ? (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "10px",
                padding: "20px",
                background: "#1e293b",
                borderRadius: "0.5rem",
                color: "#94a3b8",
              }}
            >
              <Loader2 size={20} className="spin" />
              <span>Loading nozzle configuration for Pump {pumpId}...</span>
            </div>
          ) : nozzleError ? (
            <div className="validation-errors">
              <AlertTriangle size={16} />
              <span style={{ flex: 1 }}>{nozzleError}</span>
              <button
                className="btn-outline"
                style={{ padding: "4px 12px", fontSize: "0.75rem" }}
                onClick={loadNozzleConfig}
              >
                <RefreshCw size={14} /> Retry
              </button>
            </div>
          ) : null}

          {!nozzleLoading &&
            !nozzleError &&
            nozzleRows.map((row) => (
              <div key={row.rowId} className="nozzle-card">
                <div
                  className="nozzle-card-header"
                  style={{
                    borderBottom: "1px solid #334155",
                    paddingBottom: "8px",
                    marginBottom: "12px",
                  }}
                >
                  <span
                    className="nozzle-num"
                    style={{ fontSize: "1.05rem", fontWeight: "bold" }}
                  >
                    Nozzle {row.nozzleId} ({row.fuelType})
                  </span>
                  <span
                    style={{
                      fontSize: "0.85rem",
                      color: "#94a3b8",
                      fontWeight: "500",
                    }}
                  >
                    Rate: ₹{row.rate.toFixed(2)} / L
                  </span>
                </div>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1fr 1fr 1fr",
                    gap: "8px",
                  }}
                >
                  <div
                    className="field-group"
                    style={{ marginBottom: "8px" }}
                  >
                    <label
                      className="field-label"
                      style={{ fontSize: "0.75rem" }}
                    >
                      Opening (L)
                    </label>
                    <input
                      type="number"
                      className="field-input"
                      value={row.openingReading || ""}
                      step="0.01"
                      min="0"
                      readOnly={row.isOpeningReadOnly}
                      style={
                        row.isOpeningReadOnly
                          ? {
                              backgroundColor: "#1e293b",
                              color: "#64748b",
                              border: "1px solid #334155",
                              cursor: "not-allowed",
                              padding: "6px",
                            }
                          : { padding: "6px" }
                      }
                      onChange={(e) =>
                        updateNozzle(
                          row.rowId,
                          "openingReading",
                          e.target.value,
                        )
                      }
                    />
                  </div>
                  <div
                    className="field-group"
                    style={{ marginBottom: "8px" }}
                  >
                    <label
                      className="field-label"
                      style={{ fontSize: "0.75rem" }}
                    >
                      Closing (L)
                    </label>
                    <input
                      type="number"
                      className={`field-input ${row.closingReading < row.openingReading && row.closingReading > 0 ? "input-error" : ""}`}
                      value={row.closingReading || ""}
                      step="0.01"
                      min="0"
                      style={{ padding: "6px" }}
                      onChange={(e) =>
                        updateNozzle(
                          row.rowId,
                          "closingReading",
                          e.target.value,
                        )
                      }
                    />
                  </div>
                  <div
                    className="field-group"
                    style={{ marginBottom: "8px" }}
                  >
                    <label
                      className="field-label"
                      style={{ fontSize: "0.75rem" }}
                    >
                      Testing (L)
                    </label>
                    <input
                      type="number"
                      className="field-input"
                      value={row.testing || ""}
                      step="0.01"
                      min="0"
                      style={{ padding: "6px" }}
                      onChange={(e) =>
                        updateNozzle(row.rowId, "testing", e.target.value)
                      }
                    />
                  </div>
                </div>
                <div
                  className="nozzle-sale-summary"
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontSize: "0.85rem",
                    color: "#e2e8f0",
                    marginTop: "6px",
                  }}
                >
                  <span>
                    Net Sale:{" "}
                    {Math.max(
                      0,
                      row.closingReading -
                        row.openingReading -
                        (row.testing || 0),
                    ).toFixed(2)}{" "}
                    L (Dispensed:{" "}
                    {Math.max(
                      0,
                      row.closingReading - row.openingReading,
                    ).toFixed(2)}{" "}
                    L)
                  </span>
                  <span style={{ fontWeight: "bold" }}>
                    = ₹
                    {(
                      Math.max(0, row.closingReading - row.openingReading) *
                      row.rate
                    ).toLocaleString("en-IN", { minimumFractionDigits: 2 })}
                  </span>
                </div>
              </div>
            ))}

          {!nozzleLoading && !nozzleError && nozzleRows.length > 0 && (
            <div className="gross-sales-bar" style={{ marginBottom: "24px" }}>
              <span>Gross Sales</span>
              <span className="gross-amount">
                ₹
                {grossSales.toLocaleString("en-IN", {
                  minimumFractionDigits: 2,
                })}
              </span>
            </div>
          )}

          {/* ── SECTION 2: PAYMENT COLLECTIONS ── */}
          {/* Requested Sequence: Petro Card -> Credit Card -> PhonePe -> Cash Deposit (Cash 1) -> Debtors -> Expenses -> Others -> Cash 2 */}
          <h2 className="section-heading" style={{ marginTop: "2rem" }}>
            Payment Collections
          </h2>

          {/* 1. Petro Card */}
          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              Petro Card
            </h3>

            {shiftType === "B" ? (
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1.2fr 1fr 1fr",
                  gap: "8px",
                }}
              >
                <div className="field-group">
                  <label className="field-label">Day (8am - 8pm) (₹)</label>
                  <input
                    type="number"
                    className="field-input"
                    value={petroCardDay || ""}
                    step="0.01"
                    min="0"
                    onChange={(e) => setPetroCardDay(Number(e.target.value))}
                  />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input
                    type="text"
                    className="field-input"
                    placeholder="TID"
                    value={petroCardTidDay}
                    onChange={(e) => setPetroCardTidDay(e.target.value)}
                  />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input
                    type="text"
                    className="field-input"
                    placeholder="Batch"
                    value={petroCardBatchDay}
                    onChange={(e) => setPetroCardBatchDay(e.target.value)}
                  />
                </div>
              </div>
            ) : (
              <>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1.2fr 1fr 1fr",
                    gap: "8px",
                    marginBottom: "10px",
                  }}
                >
                  <div className="field-group">
                    <label className="field-label">Morning (12am - 8pm) (₹)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={petroCardMorning || ""}
                      step="0.01"
                      min="0"
                      onChange={(e) =>
                        setPetroCardMorning(Number(e.target.value))
                      }
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">TID</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="TID"
                      value={petroCardTidMorning}
                      onChange={(e) => setPetroCardTidMorning(e.target.value)}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">Batch</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="Batch"
                      value={petroCardBatchMorning}
                      onChange={(e) =>
                        setPetroCardBatchMorning(e.target.value)
                      }
                    />
                  </div>
                </div>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1.2fr 1fr 1fr",
                    gap: "8px",
                  }}
                >
                  <div className="field-group">
                    <label className="field-label">Night (8pm - 12am) (₹)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={petroCardNight || ""}
                      step="0.01"
                      min="0"
                      onChange={(e) =>
                        setPetroCardNight(Number(e.target.value))
                      }
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">TID</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="TID"
                      value={petroCardTidNight}
                      onChange={(e) => setPetroCardTidNight(e.target.value)}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">Batch</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="Batch"
                      value={petroCardBatchNight}
                      onChange={(e) => setPetroCardBatchNight(e.target.value)}
                    />
                  </div>
                </div>
              </>
            )}
          </div>

          {/* 2. Credit Card (Card / PineLabs Credit Card) */}
          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              Credit Card (Card)
            </h3>

            {shiftType === "B" ? (
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1.2fr 1fr 1fr",
                  gap: "8px",
                }}
              >
                <div className="field-group">
                  <label className="field-label">Day (8am - 8pm) (₹)</label>
                  <input
                    type="number"
                    className="field-input"
                    value={creditCardDay || ""}
                    step="0.01"
                    min="0"
                    onChange={(e) => setCreditCardDay(Number(e.target.value))}
                  />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input
                    type="text"
                    className="field-input"
                    placeholder="TID"
                    value={creditCardTidDay}
                    onChange={(e) => setCreditCardTidDay(e.target.value)}
                  />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input
                    type="text"
                    className="field-input"
                    placeholder="Batch"
                    value={creditCardBatchDay}
                    onChange={(e) => setCreditCardBatchDay(e.target.value)}
                  />
                </div>
              </div>
            ) : (
              <>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1.2fr 1fr 1fr",
                    gap: "8px",
                    marginBottom: "10px",
                  }}
                >
                  <div className="field-group">
                    <label className="field-label">Morning (12am - 8pm) (₹)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={creditCardMorning || ""}
                      step="0.01"
                      min="0"
                      onChange={(e) => setCreditCardMorning(Number(e.target.value))}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">TID</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="TID"
                      value={creditCardTidMorning}
                      onChange={(e) => setCreditCardTidMorning(e.target.value)}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">Batch</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="Batch"
                      value={creditCardBatchMorning}
                      onChange={(e) => setCreditCardBatchMorning(e.target.value)}
                    />
                  </div>
                </div>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1.2fr 1fr 1fr",
                    gap: "8px",
                  }}
                >
                  <div className="field-group">
                    <label className="field-label">Night (8pm - 12am) (₹)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={creditCardNight || ""}
                      step="0.01"
                      min="0"
                      onChange={(e) => setCreditCardNight(Number(e.target.value))}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">TID</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="TID"
                      value={creditCardTidNight}
                      onChange={(e) => setCreditCardTidNight(e.target.value)}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">Batch</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="Batch"
                      value={creditCardBatchNight}
                      onChange={(e) => setCreditCardBatchNight(e.target.value)}
                    />
                  </div>
                </div>
              </>
            )}
          </div>

          {/* 3. PhonePe */}
          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              PhonePe
            </h3>

            {shiftType === "B" ? (
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1.2fr 1fr 1fr",
                  gap: "8px",
                }}
              >
                <div className="field-group">
                  <label className="field-label">Day (8am - 8pm) (₹)</label>
                  <input
                    type="number"
                    className="field-input"
                    value={phonePeDay || ""}
                    step="0.01"
                    min="0"
                    onChange={(e) => setPhonePeDay(Number(e.target.value))}
                  />
                </div>
                <div className="field-group">
                  <label className="field-label">TID</label>
                  <input
                    type="text"
                    className="field-input"
                    placeholder="TID"
                    value={phonePeTidDay}
                    onChange={(e) => setPhonePeTidDay(e.target.value)}
                  />
                </div>
                <div className="field-group">
                  <label className="field-label">Batch</label>
                  <input
                    type="text"
                    className="field-input"
                    placeholder="Batch"
                    value={phonePeBatchDay}
                    onChange={(e) => setPhonePeBatchDay(e.target.value)}
                  />
                </div>
              </div>
            ) : (
              <>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1.2fr 1fr 1fr",
                    gap: "8px",
                    marginBottom: "10px",
                  }}
                >
                  <div className="field-group">
                    <label className="field-label">Morning (12am - 8pm) (₹)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={phonePeMorning || ""}
                      step="0.01"
                      min="0"
                      onChange={(e) =>
                        setPhonePeMorning(Number(e.target.value))
                      }
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">TID</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="TID"
                      value={phonePeTidMorning}
                      onChange={(e) => setPhonePeTidMorning(e.target.value)}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">Batch</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="Batch"
                      value={phonePeBatchMorning}
                      onChange={(e) => setPhonePeBatchMorning(e.target.value)}
                    />
                  </div>
                </div>
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1.2fr 1fr 1fr",
                    gap: "8px",
                  }}
                >
                  <div className="field-group">
                    <label className="field-label">Night (8pm - 12am) (₹)</label>
                    <input
                      type="number"
                      className="field-input"
                      value={phonePeNight || ""}
                      step="0.01"
                      min="0"
                      onChange={(e) => setPhonePeNight(Number(e.target.value))}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">TID</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="TID"
                      value={phonePeTidNight}
                      onChange={(e) => setPhonePeTidNight(e.target.value)}
                    />
                  </div>
                  <div className="field-group">
                    <label className="field-label">Batch</label>
                    <input
                      type="text"
                      className="field-input"
                      placeholder="Batch"
                      value={phonePeBatchNight}
                      onChange={(e) => setPhonePeBatchNight(e.target.value)}
                    />
                  </div>
                </div>
              </>
            )}

            {/* Cross-DSM QR Code Option Attached to PhonePe (Hidden/Collapsible) */}
            <div style={{ marginTop: "12px", borderTop: "1px dashed rgba(148, 163, 184, 0.2)", paddingTop: "10px" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <button
                  type="button"
                  onClick={() => setShowCrossQrSection(!showCrossQrSection)}
                  style={{
                    background: "transparent",
                    border: "1px solid rgba(16, 185, 129, 0.4)",
                    borderRadius: "6px",
                    padding: "6px 12px",
                    color: showCrossQrSection ? "#10b981" : "#94a3b8",
                    fontSize: "0.8rem",
                    cursor: "pointer",
                    display: "inline-flex",
                    alignItems: "center",
                    gap: "6px",
                    fontWeight: "500",
                    transition: "all 0.2s ease",
                  }}
                >
                  <span style={{ fontSize: "0.75rem" }}>{showCrossQrSection ? "▼" : "▶"}</span>
                  <span>📱 Paid on another DSM's QR Code?</span>
                  {qrPayments.length > 0 && (
                    <span
                      style={{
                        marginLeft: "6px",
                        backgroundColor: "#10b981",
                        color: "#0f172a",
                        borderRadius: "10px",
                        padding: "1px 7px",
                        fontSize: "0.72rem",
                        fontWeight: "bold",
                      }}
                    >
                      {qrPayments.length} Added (₹{qrTotal.toFixed(2)})
                    </span>
                  )}
                </button>
              </div>

              {showCrossQrSection && (
                <div
                  style={{
                    marginTop: "10px",
                    padding: "12px",
                    backgroundColor: "rgba(16, 185, 129, 0.06)",
                    borderRadius: "8px",
                    border: "1px solid rgba(16, 185, 129, 0.2)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      marginBottom: "10px",
                    }}
                  >
                    <span
                      style={{
                        fontSize: "0.85rem",
                        fontWeight: "bold",
                        color: "#10b981",
                      }}
                    >
                      📱 Cross-DSM QR Entry
                    </span>
                    <span style={{ fontSize: "0.75rem", color: "#94a3b8" }}>
                      (Record payment received on another DSM's QR)
                    </span>
                  </div>

                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns: "1.2fr 1fr",
                      gap: "8px",
                      marginBottom: "8px",
                    }}
                  >
                    <div className="field-group">
                      <label className="field-label">Paid on DSM's QR</label>
                      <select
                        className="field-input"
                        value={newQrTargetDsm}
                        onChange={(e) => setNewQrTargetDsm(e.target.value)}
                      >
                        <option value="">-- Select DSM --</option>
                        {dsmUsersList.map((u) => (
                          <option key={u.id} value={u.fullName}>
                            {u.fullName}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="field-group">
                      <label className="field-label">Amount (₹)</label>
                      <input
                        type="number"
                        step="0.01"
                        min="0"
                        className="field-input"
                        placeholder="0.00"
                        value={newQrAmount}
                        onChange={(e) => setNewQrAmount(e.target.value)}
                      />
                    </div>
                  </div>

                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns: "1fr 1fr 80px",
                      gap: "8px",
                      marginBottom: "8px",
                    }}
                  >
                    <div className="field-group">
                      <label className="field-label">TID (Optional)</label>
                      <input
                        type="text"
                        className="field-input"
                        placeholder="TID"
                        value={newQrTid}
                        onChange={(e) => setNewQrTid(e.target.value)}
                      />
                    </div>
                    <div className="field-group">
                      <label className="field-label">Batch (Optional)</label>
                      <input
                        type="text"
                        className="field-input"
                        placeholder="Batch"
                        value={newQrBatch}
                        onChange={(e) => setNewQrBatch(e.target.value)}
                      />
                    </div>
                    <div
                      style={{
                        display: "flex",
                        alignItems: "flex-end",
                      }}
                    >
                      <button
                        type="button"
                        className="btn btn-primary"
                        style={{
                          height: "38px",
                          width: "100%",
                          padding: "0",
                          fontSize: "0.85rem",
                        }}
                        onClick={() => {
                          const amt = parseFloat(newQrAmount);
                          if (!newQrTargetDsm) {
                            alert("Please select the DSM whose QR was scanned.");
                            return;
                          }
                          if (isNaN(amt) || amt <= 0) {
                            alert("Please enter a valid QR payment amount.");
                            return;
                          }
                          setQrPayments((prev) => [
                            ...prev,
                            {
                              targetDsmName: newQrTargetDsm,
                              amount: amt,
                              tid: newQrTid.trim() || undefined,
                              batch: newQrBatch.trim() || undefined,
                              slot: shiftType === "B" ? "Day" : "Morning",
                            },
                          ]);
                          setNewQrTargetDsm("");
                          setNewQrAmount("");
                          setNewQrTid("");
                          setNewQrBatch("");
                        }}
                      >
                        + Add
                      </button>
                    </div>
                  </div>

                  {/* List of Added QR Payments */}
                  {qrPayments.length > 0 && (
                    <div style={{ marginTop: "10px" }}>
                      <table
                        style={{
                          width: "100%",
                          fontSize: "0.8rem",
                          borderCollapse: "collapse",
                        }}
                      >
                        <thead>
                          <tr
                            style={{
                              borderBottom: "1px solid #334155",
                              textAlign: "left",
                              color: "#94a3b8",
                            }}
                          >
                            <th style={{ padding: "4px" }}>DSM QR</th>
                            <th style={{ padding: "4px" }}>Amount</th>
                            <th style={{ padding: "4px" }}>TID/Batch</th>
                            <th style={{ padding: "4px", width: "30px" }}></th>
                          </tr>
                        </thead>
                        <tbody>
                          {qrPayments.map((item, idx) => (
                            <tr
                              key={idx}
                              style={{ borderBottom: "1px solid #1e293b" }}
                            >
                              <td style={{ padding: "6px 4px", fontWeight: "500" }}>
                                {item.targetDsmName}
                              </td>
                              <td
                                style={{
                                  padding: "6px 4px",
                                  color: "#10b981",
                                  fontWeight: "bold",
                                }}
                              >
                                ₹{item.amount.toFixed(2)}
                              </td>
                              <td style={{ padding: "6px 4px", color: "#64748b" }}>
                                {item.tid ? `TID: ${item.tid}` : ""}
                                {item.batch ? ` B:${item.batch}` : ""}
                              </td>
                              <td style={{ padding: "6px 4px" }}>
                                <button
                                  type="button"
                                  style={{
                                    background: "none",
                                    border: "none",
                                    color: "#ef4444",
                                    cursor: "pointer",
                                    fontSize: "1rem",
                                  }}
                                  onClick={() =>
                                    setQrPayments((prev) =>
                                      prev.filter((_, i) => i !== idx),
                                    )
                                  }
                                >
                                  ✕
                                </button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      <div
                        style={{
                          textAlign: "right",
                          fontSize: "0.85rem",
                          fontWeight: "bold",
                          color: "#10b981",
                          marginTop: "6px",
                        }}
                      >
                        Cross-DSM Total: ₹{qrTotal.toFixed(2)}
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>

          {/* 4. Cash Deposit (Cash 1) */}
          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              Cash Deposit (Cash 1)
            </h3>
            <div className="field-group" style={{ marginBottom: "8px" }}>
              <label className="field-label">Total Cash 1 Deposit Amount (₹)</label>
              <input
                type="number"
                step="0.01"
                min="0"
                className="field-input"
                placeholder="Enter Cash 1 Amount (₹)"
                value={cash1Amount || ""}
                onChange={(e) => setCash1Amount(Math.max(0, parseFloat(e.target.value) || 0))}
              />
            </div>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                borderTop: "1px solid #334155",
                paddingTop: "10px",
                marginTop: "10px",
                fontSize: "1rem",
                fontWeight: "bold",
                color: "#10b981",
              }}
            >
              <span>Total Cash Deposit:</span>
              <span>
                ₹
                {cashDeposit.toLocaleString("en-IN", {
                  minimumFractionDigits: 2,
                })}
              </span>
            </div>
          </div>

          {/* 5. Debtors Register */}
          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              Debtors Register (Credit Sales)
            </h3>
            <div className="field-row-2">
              <div className="field-group">
                <label className="field-label">Debtor Name</label>
                <select
                  className="field-input"
                  value={selectedCreditorId}
                  onChange={(e) => {
                    const val = e.target.value;
                    setSelectedCreditorId(val);
                    const c = creditorList.find((x) => x.id === val);
                    setNewDebtorName(c ? c.name : "");
                    setNewDebtorVehicle("");
                  }}
                >
                  <option value="">-- Select Debtor --</option>
                  {creditorList.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field-group">
                <label className="field-label">Amount (₹)</label>
                <input
                  type="number"
                  step="0.01"
                  className="field-input"
                  placeholder="0.00"
                  value={newDebtorAmount}
                  onChange={(e) => setNewDebtorAmount(e.target.value)}
                />
              </div>
            </div>
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "8px",
                margin: "10px 0 6px 0",
              }}
            >
              <input
                type="checkbox"
                id="custom-vehicle-check-single"
                checked={customVehicle}
                onChange={(e) => {
                  setCustomVehicle(e.target.checked);
                  setNewDebtorVehicle("");
                }}
              />
              <label
                htmlFor="custom-vehicle-check-single"
                style={{
                  fontSize: "0.85rem",
                  fontWeight: "500",
                  color: "#94a3b8",
                  cursor: "pointer",
                }}
              >
                Type custom vehicle number
              </label>
            </div>
            <div className="field-row-2">
              <div className="field-group">
                <label className="field-label">Vehicle No.</label>
                {customVehicle ? (
                  <input
                    type="text"
                    className="field-input"
                    placeholder="MH-12-XX-XXXX"
                    value={newDebtorVehicle}
                    onChange={(e) => setNewDebtorVehicle(e.target.value)}
                  />
                ) : (
                  <select
                    className="field-input"
                    value={newDebtorVehicle}
                    onChange={(e) => setNewDebtorVehicle(e.target.value)}
                    disabled={!selectedCreditorId}
                  >
                    <option value="">-- Select Vehicle --</option>
                    {vehicleList
                      .filter((v) => v.creditorId === selectedCreditorId)
                      .map((v, i) => (
                        <option key={i} value={v.vehicleNumber}>
                          {v.vehicleNumber}
                        </option>
                      ))}
                  </select>
                )}
              </div>
              <div className="field-group">
                <label className="field-label">Slip No.</label>
                <input
                  type="text"
                  className="field-input"
                  placeholder="Slip No."
                  value={newDebtorSlip}
                  onChange={(e) => setNewDebtorSlip(e.target.value)}
                />
              </div>
            </div>
            <button
              type="button"
              className="btn-outline"
              style={{ marginTop: "12px", width: "100%", padding: "8px" }}
              onClick={() => {
                if (
                  !newDebtorName ||
                  !newDebtorAmount ||
                  Number(newDebtorAmount) <= 0
                )
                  return;

                const time = new Date().toLocaleTimeString("en-US", {
                  hour12: false,
                  hour: "2-digit",
                  minute: "2-digit",
                });
                setDebtorEntries((prev) => [
                  ...prev,
                  {
                    debtorName: newDebtorName,
                    amount: Number(newDebtorAmount),
                    vehicleNumber: newDebtorVehicle,
                    slipNumber: newDebtorSlip,
                    time,
                  },
                ]);

                setNewDebtorAmount("");
                setNewDebtorVehicle("");
                setNewDebtorSlip("");
                setSelectedCreditorId("");
                setNewDebtorName("");
                setCustomVehicle(false);
              }}
            >
              + Add Debtor Entry
            </button>
            {debtorEntries.length > 0 && (
              <div
                className="debtor-entry-list"
                style={{ marginTop: "16px" }}
              >
                {debtorEntries.map((item, idx) => (
                  <div
                    key={idx}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      background: "#0f172a",
                      padding: "8px 12px",
                      borderRadius: "4px",
                      marginBottom: "4px",
                      fontSize: "0.85rem",
                    }}
                  >
                    <div>
                      <strong>{item.debtorName}</strong>: ₹
                      {item.amount.toFixed(2)} <br />
                      <span style={{ color: "#94a3b8" }}>
                        Veh: {item.vehicleNumber || "N/A"} | Slip:{" "}
                        {item.slipNumber || "N/A"} | Time: {item.time}
                      </span>
                    </div>
                    <button
                      type="button"
                      style={{
                        color: "#ef4444",
                        background: "none",
                        border: "none",
                        cursor: "pointer",
                        fontWeight: "bold",
                      }}
                      onClick={() => {
                        setDebtorEntries((prev) =>
                          prev.filter((_, i) => i !== idx),
                        );
                      }}
                    >
                      Delete
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* 6. Expenses */}
          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
              }}
            >
              <span>Expenses / Adjustments</span>
              <span
                style={{
                  fontSize: "0.85rem",
                  fontWeight: "normal",
                  color: "#f87171",
                }}
              >
                Total: ₹{expense.toFixed(2)}
              </span>
            </h3>

            <div className="field-group" style={{ marginBottom: "12px" }}>
              <label className="field-label">Expense Description / Purpose</label>
              <input
                type="text"
                className="field-input"
                placeholder="e.g. Tea / Refreshment, Dinner, Repair"
                value={newExpenseDescription}
                onChange={(e) => setNewExpenseDescription(e.target.value)}
              />
            </div>
            <div className="field-group" style={{ marginBottom: "12px" }}>
              <label className="field-label">Amount (₹)</label>
              <input
                type="number"
                step="0.01"
                min="0"
                className="field-input"
                placeholder="0.00"
                value={newExpenseAmount}
                onChange={(e) => setNewExpenseAmount(e.target.value)}
              />
            </div>

            <button
              type="button"
              className="btn-outline"
              style={{ marginTop: "4px", width: "100%", padding: "8px" }}
              onClick={() => {
                if (
                  !newExpenseDescription.trim() ||
                  !newExpenseAmount ||
                  Number(newExpenseAmount) <= 0
                )
                  return;
                setExpenseEntries((prev) => [
                  ...prev,
                  {
                    description: newExpenseDescription.trim(),
                    amount: Number(newExpenseAmount),
                  },
                ]);
                setNewExpenseDescription("");
                setNewExpenseAmount("");
              }}
            >
              + Add Expense Entry
            </button>

            {expenseEntries.length > 0 && (
              <div
                className="debtor-entry-list"
                style={{ marginTop: "16px" }}
              >
                {expenseEntries.map((item, idx) => (
                  <div
                    key={idx}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      background: "#0f172a",
                      padding: "8px 12px",
                      borderRadius: "4px",
                      marginBottom: "4px",
                      fontSize: "0.85rem",
                    }}
                  >
                    <div>
                      <strong style={{ color: "#f87171" }}>
                        ₹{item.amount.toFixed(2)}
                      </strong>{" "}
                      <span>{item.description}</span>
                    </div>
                    <button
                      type="button"
                      style={{
                        background: "transparent",
                        border: "none",
                        color: "#ef4444",
                        cursor: "pointer",
                        fontWeight: "bold",
                        fontSize: "1rem",
                      }}
                      onClick={() =>
                        setExpenseEntries((prev) =>
                          prev.filter((_, i) => i !== idx),
                        )
                      }
                    >
                      ✕
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* 7. Others */}
          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              Other Collections
            </h3>
            <div className="field-row">
              <div className="field-group" style={{ marginBottom: 0 }}>
                <label className="field-label">
                  Others (Not in Total) (₹)
                </label>
                <input
                  type="number"
                  className="field-input"
                  value={others || ""}
                  step="0.01"
                  min="0"
                  onChange={(e) => setOthers(Number(e.target.value))}
                />
              </div>
            </div>
          </div>

          {/* 8. Cash 2 (Cash in Hand / Denominations) - AT THE END OF COLLECTIONS */}
          {/* ── SECTION 3: KANDHARE PETROLEUM LEDGER ── */}
          <h2 className="section-heading" style={{ marginTop: "2rem" }}>
            Kandhare Petroleum Ledger
          </h2>

          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              Kandhare Petroleum
            </h3>
            <div className="field-row-2">
              <div className="field-group">
                <label className="field-label">Person Name</label>
                <input
                  type="text"
                  className="field-input"
                  placeholder="Enter name"
                  value={newKpName}
                  onChange={(e) => setNewKpName(e.target.value)}
                />
              </div>
              <div className="field-group">
                <label className="field-label">Vehicle Number</label>
                <input
                  type="text"
                  className="field-input"
                  placeholder="Enter vehicle no"
                  value={newKpVehicleNumber}
                  onChange={(e) => setNewKpVehicleNumber(e.target.value)}
                />
              </div>
            </div>
            <div className="field-row-2" style={{ marginTop: "12px" }}>
              <div className="field-group">
                <label className="field-label">Slip Number</label>
                <input
                  type="text"
                  className="field-input"
                  placeholder="Enter slip no"
                  value={newKpSlipNumber}
                  onChange={(e) => setNewKpSlipNumber(e.target.value)}
                />
              </div>
              <div className="field-group">
                <label className="field-label">Amount (₹)</label>
                <input
                  type="number"
                  step="0.01"
                  className="field-input"
                  placeholder="0.00"
                  value={newKpAmount}
                  onChange={(e) => setNewKpAmount(e.target.value)}
                />
              </div>
            </div>

            <button
              type="button"
              className="btn-outline"
              style={{ marginTop: "12px", width: "100%", padding: "8px" }}
              onClick={() => {
                if (!newKpName.trim() || !newKpAmount || Number(newKpAmount) <= 0)
                  return;
                setKhandhareEntries((prev) => [
                  ...prev,
                  {
                    name: newKpName,
                    vehicleNumber: newKpVehicleNumber,
                    slipNumber: newKpSlipNumber,
                    amount: Number(newKpAmount),
                  },
                ]);
                setNewKpName("");
                setNewKpVehicleNumber("");
                setNewKpSlipNumber("");
                setNewKpAmount("");
              }}
            >
              + Add Kandhare Petroleum Entry
            </button>

            {khandhareEntries.length > 0 && (
              <div
                className="debtor-entry-list"
                style={{ marginTop: "16px" }}
              >
                {khandhareEntries.map((item, idx) => (
                  <div
                    key={idx}
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      background: "#0f172a",
                      padding: "8px 12px",
                      borderRadius: "4px",
                      marginBottom: "4px",
                      fontSize: "0.85rem",
                    }}
                  >
                    <div>
                      <strong>₹{item.amount.toFixed(2)}</strong>{" "}
                      <span>{item.name}</span>
                      <div style={{ color: "#94a3b8" }}>
                        {item.vehicleNumber && <span>Vehicle: {item.vehicleNumber} | </span>}
                        {item.slipNumber ? `Slip: ${item.slipNumber}` : "No slip number"}
                      </div>
                    </div>
                    <button
                      type="button"
                      style={{
                        color: "#ef4444",
                        background: "none",
                        border: "none",
                        cursor: "pointer",
                        fontWeight: "bold",
                      }}
                      onClick={() => {
                        setKhandhareEntries((prev) =>
                          prev.filter((_, i) => i !== idx),
                        );
                      }}
                    >
                      Delete
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div
            className="nozzle-card"
            style={{ marginBottom: "16px", padding: "16px" }}
          >
            <h3
              style={{
                fontSize: "1rem",
                fontWeight: "bold",
                marginBottom: "12px",
                borderBottom: "1px solid #334155",
                paddingBottom: "6px",
              }}
            >
              Cash 2 - Cash in Hand (Denominations)
            </h3>

            <div
              style={{
                display: "grid",
                gridTemplateColumns: "1fr 1fr",
                gap: "10px 16px",
                marginBottom: "12px",
              }}
            >
              <div
                style={{ display: "flex", alignItems: "center", gap: "8px" }}
              >
                <span
                  style={{
                    width: "45px",
                    fontWeight: "bold",
                    fontSize: "0.9rem",
                  }}
                >
                  ₹500 x
                </span>
                <input
                  type="number"
                  className="field-input"
                  style={{ padding: "6px" }}
                  value={denom500 || ""}
                  min="0"
                  onChange={(e) =>
                    setDenom500(Math.max(0, parseInt(e.target.value) || 0))
                  }
                />
              </div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "8px" }}
              >
                <span
                  style={{
                    width: "45px",
                    fontWeight: "bold",
                    fontSize: "0.9rem",
                  }}
                >
                  ₹200 x
                </span>
                <input
                  type="number"
                  className="field-input"
                  style={{ padding: "6px" }}
                  value={denom200 || ""}
                  min="0"
                  onChange={(e) =>
                    setDenom200(Math.max(0, parseInt(e.target.value) || 0))
                  }
                />
              </div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "8px" }}
              >
                <span
                  style={{
                    width: "45px",
                    fontWeight: "bold",
                    fontSize: "0.9rem",
                  }}
                >
                  ₹100 x
                </span>
                <input
                  type="number"
                  className="field-input"
                  style={{ padding: "6px" }}
                  value={denom100 || ""}
                  min="0"
                  onChange={(e) =>
                    setDenom100(Math.max(0, parseInt(e.target.value) || 0))
                  }
                />
              </div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "8px" }}
              >
                <span
                  style={{
                    width: "45px",
                    fontWeight: "bold",
                    fontSize: "0.9rem",
                  }}
                >
                  ₹50 x
                </span>
                <input
                  type="number"
                  className="field-input"
                  style={{ padding: "6px" }}
                  value={denom50 || ""}
                  min="0"
                  onChange={(e) =>
                    setDenom50(Math.max(0, parseInt(e.target.value) || 0))
                  }
                />
              </div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "8px" }}
              >
                <span
                  style={{
                    width: "45px",
                    fontWeight: "bold",
                    fontSize: "0.9rem",
                  }}
                >
                  ₹20 x
                </span>
                <input
                  type="number"
                  className="field-input"
                  style={{ padding: "6px" }}
                  value={denom20 || ""}
                  min="0"
                  onChange={(e) =>
                    setDenom20(Math.max(0, parseInt(e.target.value) || 0))
                  }
                />
              </div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "8px" }}
              >
                <span
                  style={{
                    width: "45px",
                    fontWeight: "bold",
                    fontSize: "0.9rem",
                  }}
                >
                  ₹10 x
                </span>
                <input
                  type="number"
                  className="field-input"
                  style={{ padding: "6px" }}
                  value={denom10 || ""}
                  min="0"
                  onChange={(e) =>
                    setDenom10(Math.max(0, parseInt(e.target.value) || 0))
                  }
                />
              </div>
              <div
                style={{ display: "flex", alignItems: "center", gap: "8px" }}
              >
                <span
                  style={{
                    width: "45px",
                    fontWeight: "bold",
                    fontSize: "0.9rem",
                  }}
                >
                  Coins
                </span>
                <input
                  type="number"
                  className="field-input"
                  style={{ padding: "6px" }}
                  value={coins || ""}
                  min="0"
                  onChange={(e) =>
                    setCoins(Math.max(0, parseFloat(e.target.value) || 0))
                  }
                />
              </div>
            </div>

            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                borderTop: "1px solid #334155",
                paddingTop: "10px",
                marginTop: "10px",
                fontSize: "1rem",
                fontWeight: "bold",
                color: "#10b981",
              }}
            >
              <span>Total Cash in Hand (Cash 2):</span>
              <span>
                ₹
                {cash.toLocaleString("en-IN", {
                  minimumFractionDigits: 2,
                })}
              </span>
            </div>
          </div>

          {/* ── SECTION 4: OIL & DEF PRODUCT SALES (IF AVAILABLE) ── */}
          {availableProducts.length > 0 && (
            <div
              className="nozzle-card"
              style={{ marginBottom: "16px", padding: "16px" }}
            >
              <h3
                style={{
                  fontSize: "1rem",
                  fontWeight: "bold",
                  marginBottom: "12px",
                  borderBottom: "1px solid #334155",
                  paddingBottom: "6px",
                }}
              >
                Oil &amp; DEF Product Sales
              </h3>
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "12px",
                }}
              >
                {availableProducts.map((p) => {
                  const stock = productStocks[p.id] || 0;
                  const qty = salesQuantities[p.id] || 0;
                  const total = qty * p.defaultSaleRate;

                  return (
                    <div
                      key={p.id}
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        gap: "8px",
                        borderBottom: "1px solid #1e293b",
                        paddingBottom: "8px",
                      }}
                    >
                      <div style={{ flex: "1" }}>
                        <div
                          style={{ fontWeight: "500", fontSize: "0.9rem" }}
                        >
                          {p.productName}
                        </div>
                        <div
                          style={{ fontSize: "0.75rem", color: "#94a3b8" }}
                        >
                          Price: ₹{p.defaultSaleRate.toFixed(2)} / {p.unit} |
                          Stock:{" "}
                          <span
                            style={{
                              color: stock > 0 ? "#4ade80" : "#f87171",
                              fontWeight: "bold",
                            }}
                          >
                            {stock} {p.unit}
                          </span>
                        </div>
                      </div>
                      <div style={{ width: "100px" }}>
                        <input
                          type="number"
                          min="0"
                          max={stock}
                          step="any"
                          className="field-input"
                          style={{
                            padding: "6px 8px",
                            fontSize: "0.85rem",
                            textAlign: "right",
                          }}
                          placeholder="0"
                          value={salesQuantities[p.id] || ""}
                          onChange={(e) => {
                            const val = parseFloat(e.target.value);
                            const cleanVal = isNaN(val) ? 0 : val;
                            if (cleanVal < 0) return;
                            if (cleanVal > stock) {
                              alert(
                                `Cannot sell more than available stock of ${stock} ${p.unit}.`,
                              );
                              return;
                            }
                            setSalesQuantities((prev) => ({
                              ...prev,
                              [p.id]: cleanVal,
                            }));
                          }}
                        />
                      </div>
                      <div
                        style={{
                          width: "80px",
                          textAlign: "right",
                          fontSize: "0.9rem",
                          fontWeight: "bold",
                        }}
                      >
                        ₹{total.toFixed(2)}
                      </div>
                    </div>
                  );
                })}
              </div>

              <div
                style={{
                  marginTop: "12px",
                  borderTop: "1px dashed #334155",
                  paddingTop: "10px",
                  fontSize: "0.85rem",
                  color: "#94a3b8",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    marginBottom: "4px",
                  }}
                >
                  <span>Shift Oil Total:</span>
                  <span>₹{shiftOilTotal.toFixed(2)}</span>
                </div>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    marginBottom: "4px",
                  }}
                >
                  <span>Shift DEF Total:</span>
                  <span>₹{shiftDefTotal.toFixed(2)}</span>
                </div>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    fontWeight: "bold",
                    color: "#fff",
                    fontSize: "0.95rem",
                    marginTop: "6px",
                    borderTop: "1px solid #334155",
                    paddingTop: "6px",
                  }}
                >
                  <span>Grand Product Sales:</span>
                  <span>₹{grandProductSales.toFixed(2)}</span>
                </div>
              </div>
            </div>
          )}



          {/* ── SECTION 6: SHIFT SUMMARY & SUBMISSION ── */}
          <h2 className="section-heading" style={{ marginTop: "2rem" }}>
            Shift Summary &amp; Submission
          </h2>

          <div
            className={`mismatch-preview ${Math.abs(mismatch) > 500 ? "mismatch-warn" : "mismatch-ok"}`}
            style={{ marginBottom: "16px" }}
          >
            <div className="mismatch-row">
              <span>Gross Sales (Fuel)</span>
              <span>
                ₹
                {grossSales.toLocaleString("en-IN", {
                  minimumFractionDigits: 2,
                })}
              </span>
            </div>
            {grandProductSales > 0 && (
              <div className="mismatch-row">
                <span>Product Sales (Oil/DEF)</span>
                <span>
                  ₹
                  {grandProductSales.toLocaleString("en-IN", {
                    minimumFractionDigits: 2,
                  })}
                </span>
              </div>
            )}
            <div className="mismatch-row">
              <span>Total Collections</span>
              <span>
                ₹
                {totalCollections.toLocaleString("en-IN", {
                  minimumFractionDigits: 2,
                })}
              </span>
            </div>
            <div className="mismatch-row mismatch-total">
              <span>Mismatch</span>
              <span
                className={Math.abs(mismatch) > 500 ? "text-warn" : "text-ok"}
              >
                ₹{mismatch.toFixed(2)}
              </span>
            </div>
          </div>

          <div className="field-group" style={{ marginBottom: "16px" }}>
            <label className="field-label" htmlFor="submission-notes">
              Notes / Remarks (optional)
            </label>
            <textarea
              id="submission-notes"
              className="field-input field-textarea"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="Any remarks for the manager..."
              rows={3}
            />
          </div>

          {validationErrors.length > 0 && (
            <div
              id="validation-errors-box"
              className="validation-errors"
              style={{ marginBottom: "16px" }}
            >
              <AlertTriangle size={16} />
              <ul>
                {validationErrors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          )}

          {error && (
            <div className="alert-error" role="alert" style={{ marginBottom: "16px" }}>
              <AlertTriangle size={16} />
              <span>{error}</span>
            </div>
          )}

          {!online && (
            <div className="alert-info" style={{ marginBottom: "16px" }}>
              <WifiOff size={16} />
              <span>
                You are offline. This entry will be saved locally and
                submitted when online.
              </span>
            </div>
          )}

          <div className="btn-row" style={{ marginTop: "16px" }}>
            <button
              id="submit-btn"
              className={`btn-primary ${isSubmitting || syncing ? "btn-loading" : ""}`}
              style={{ width: "100%", padding: "14px", fontSize: "1.1rem" }}
              onClick={handleSubmit}
              disabled={isSubmitting || syncing || nozzleLoading || nozzleRows.length === 0}
            >
              {isSubmitting || syncing ? (
                <Loader2 size={20} className="spin" />
              ) : (
                <Send size={20} />
              )}
              {isSubmitting || syncing
                ? "Submitting Shift Entry..."
                : online
                  ? "Submit Shift Entry"
                  : "Save Shift Entry Offline"}
            </button>
          </div>
        </div>
      </main>
    </div>
  );
}
