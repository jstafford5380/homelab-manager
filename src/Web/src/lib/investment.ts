export type InvestmentTotal = { currency: string; amount: number };

export function formatInvestment(totals: InvestmentTotal[] | null | undefined) {
  if (!totals) return "Unavailable";
  if (!totals.length) return "No costs recorded";
  return totals.map(({ currency, amount }) =>
    amount.toLocaleString(undefined, { style: "currency", currency, currencyDisplay: totals.length > 1 ? "code" : "symbol" })
  ).join(" · ");
}
