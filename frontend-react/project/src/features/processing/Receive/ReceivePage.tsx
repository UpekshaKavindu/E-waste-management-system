import React from 'react';
import { useSearchParams } from 'react-router-dom';
import { PackagePlus, Recycle, Truck } from 'lucide-react';
import JobReceiveForm from './JobReceiveForm';
import ExtraWasteReceiveForm from './ExtraWasteReceiveForm';
import { PageHeader } from '../components';

type Tab = 'job' | 'extra';

const TABS: { id: Tab; label: string; hint: string; icon: React.ElementType }[] = [
  { id: 'job', label: 'Job collection', hint: 'One or more completed jobs from a collector', icon: Truck },
  { id: 'extra', label: 'Extra waste', hint: 'Walk-in drop-off brought by a collector', icon: Recycle },
];

const ReceivePage: React.FC = () => {
  const [params, setParams] = useSearchParams();
  const tabParam = params.get('tab');
  const tab: Tab = tabParam === 'extra' ? 'extra' : 'job';

  const selectTab = (next: Tab) =>
    setParams(next === 'job' ? {} : { tab: next }, { replace: true });

  return (
    <div>
      <PageHeader
        title="Receive waste"
        subtitle="Weigh new deliveries into the warehouse. Each accepted delivery becomes inventory and raises a collector payment."
        icon={PackagePlus}
      />

      <div role="tablist" aria-label="Receiving type" className="mb-5 grid gap-2 sm:grid-cols-2">
        {TABS.map(({ id, label, hint, icon: Icon }) => {
          const active = tab === id;
          return (
            <button
              key={id}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => selectTab(id)}
              className={[
                'flex items-center gap-3 rounded-2xl border p-3.5 text-left transition',
                active ? 'border-mint-400 bg-white/80 shadow-md shadow-mint-500/10 ring-2 ring-mint-200' : 'border-transparent bg-white/40 hover:bg-white/60',
              ].join(' ')}
            >
              <span className={`flex h-9 w-9 items-center justify-center rounded-xl ${active ? 'bg-mint-600 text-white' : 'bg-mint-50 text-mint-700'}`}>
                <Icon size={18} />
              </span>
              <span>
                <span className="block text-sm font-semibold text-ink-900">{label}</span>
                <span className="block text-xs text-ink-600">{hint}</span>
              </span>
            </button>
          );
        })}
      </div>

      {/* The two forms stay mounted so switching tabs never throws away a half-filled receipt. */}
      <div role="tabpanel" hidden={tab !== 'job'}>
        <JobReceiveForm />
      </div>
      <div role="tabpanel" hidden={tab !== 'extra'}>
        <ExtraWasteReceiveForm />
      </div>
    </div>
  );
};

export default ReceivePage;
