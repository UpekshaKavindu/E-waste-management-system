import React, { useCallback, useEffect, useState } from 'react';
import { Pencil, RefreshCw, ShieldCheck, Trash2, UserPlus } from 'lucide-react';
import { adminAccountsApi, type AdminAccount, type AdminAccountInput } from './adminAccountsApi';
import { useCurrentUser } from '../processing/hooks/useCurrentUser';
import { getApiErrorMessage } from '../processing/utils/apiError';
import { formatDate } from '../processing/utils/format';
import {
  EmptyState,
  ErrorMessage,
  GlassCard,
  LoadingState,
  Modal,
  Notice,
  PageHeader,
  btnDanger,
  btnPrimary,
  btnSecondary,
  btnSmall,
  inputClass,
  labelClass,
  tableCellClass,
  tableHeadClass,
} from '../processing/components';

const EMPTY_FORM: AdminAccountInput = { fullName: '', email: '', phone: '', password: '' };

/** Admin only: add and remove admin accounts, and edit your own. Admins cannot self-register. */
const AdminsPage: React.FC = () => {
  const me = useCurrentUser();
  const [admins, setAdmins] = useState<AdminAccount[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // null = closed, 'new' = add form, otherwise the admin being edited
  const [editing, setEditing] = useState<AdminAccount | 'new' | null>(null);
  const [form, setForm] = useState<AdminAccountInput>(EMPTY_FORM);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const [toDelete, setToDelete] = useState<AdminAccount | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const isNew = editing === 'new';

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setAdmins(await adminAccountsApi.list());
    } catch (e) {
      setError(getApiErrorMessage(e, 'Failed to load admins.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const set = (key: keyof AdminAccountInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [key]: e.target.value }));

  const openAdd = () => {
    setForm(EMPTY_FORM);
    setFormError(null);
    setEditing('new');
  };

  const openEdit = (a: AdminAccount) => {
    setForm({ fullName: a.fullName, email: a.email, phone: a.phone ?? '', password: '' });
    setFormError(null);
    setEditing(a);
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (editing === null) return;
    if ((isNew || form.password) && form.password.length < 6) {
      setFormError('The password must be at least 6 characters.');
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      if (editing === 'new') {
        const created = await adminAccountsApi.create(form);
        setNotice(`${created.fullName} was added as an admin.`);
      } else {
        const updated = await adminAccountsApi.update(editing.userId, form);
        setNotice(`${updated.fullName}'s details were updated.`);
      }
      setEditing(null);
      load();
    } catch (err) {
      setFormError(getApiErrorMessage(err, isNew ? 'The admin could not be added.' : 'The admin could not be updated.'));
    } finally {
      setSaving(false);
    }
  };

  const confirmDelete = async () => {
    if (!toDelete) return;
    setDeleting(true);
    setDeleteError(null);
    try {
      await adminAccountsApi.remove(toDelete.userId);
      setNotice(`${toDelete.fullName} was removed and can no longer sign in.`);
      setToDelete(null);
      load();
    } catch (err) {
      setDeleteError(getApiErrorMessage(err, 'The admin could not be removed.'));
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div>
      <PageHeader
        title="Admins"
        subtitle="Admin accounts cannot be self-registered. Add or remove them here; you can edit only your own details."
        icon={ShieldCheck}
        actions={
          <>
            <button type="button" onClick={load} className={btnSecondary} disabled={loading}>
              <RefreshCw size={14} className={loading ? 'animate-spin' : ''} /> Refresh
            </button>
            <button type="button" onClick={openAdd} className={btnPrimary}>
              <UserPlus size={14} /> Add admin
            </button>
          </>
        }
      />

      {notice && (
        <Notice tone="success" className="mb-4">
          {notice}
        </Notice>
      )}

      <GlassCard padded={false}>
        {error ? (
          <div className="p-5">
            <ErrorMessage message={error} onRetry={load} />
          </div>
        ) : loading && admins.length === 0 ? (
          <LoadingState label="Loading admins…" />
        ) : admins.length === 0 ? (
          <EmptyState icon={ShieldCheck} title="No admins" description="Add an admin account to get started." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] border-collapse">
              <thead>
                <tr className="border-b border-mint-100">
                  <th className={`${tableHeadClass} px-4 py-3`}>Name</th>
                  <th className={`${tableHeadClass} px-4 py-3`}>Email</th>
                  <th className={`${tableHeadClass} px-4 py-3`}>Phone</th>
                  <th className={`${tableHeadClass} px-4 py-3`}>Added</th>
                  <th className={`${tableHeadClass} px-4 py-3 text-right`}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {admins.map((a) => (
                  <tr key={a.userId} className="border-b border-mint-50 last:border-0">
                    <td className={`${tableCellClass} font-semibold text-ink-900`}>
                      {a.fullName}
                      {a.userId === me?.userId && <span className="ml-2 text-xs font-normal text-ink-600">(you)</span>}
                    </td>
                    <td className={tableCellClass}>{a.email}</td>
                    <td className={tableCellClass}>{a.phone || '—'}</td>
                    <td className={`${tableCellClass} whitespace-nowrap`}>{formatDate(a.createdAt)}</td>
                    <td className={`${tableCellClass} text-right`}>
                      <div className="inline-flex gap-2">
                        {a.userId === me?.userId ? (
                          <button type="button" className={`${btnSecondary} ${btnSmall}`} onClick={() => openEdit(a)}>
                            <Pencil size={12} /> Edit
                          </button>
                        ) : (
                          <button
                            type="button"
                            className={`${btnSecondary} ${btnSmall}`}
                            onClick={() => {
                              setDeleteError(null);
                              setToDelete(a);
                            }}
                          >
                            <Trash2 size={12} /> Delete
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </GlassCard>

      <Modal
        open={editing !== null}
        onClose={() => setEditing(null)}
        title={isNew ? 'Add admin' : 'Edit admin'}
        busy={saving}
        footer={
          <>
            <button type="button" className={btnSecondary} onClick={() => setEditing(null)} disabled={saving}>
              Cancel
            </button>
            <button type="submit" form="admin-form" className={btnPrimary} disabled={saving}>
              {saving ? 'Saving…' : isNew ? 'Add admin' : 'Save changes'}
            </button>
          </>
        }
      >
        <form id="admin-form" onSubmit={submit} className="space-y-4">
          <div>
            <label className={labelClass} htmlFor="admin-name">Full name</label>
            <input id="admin-name" value={form.fullName} onChange={set('fullName')} required maxLength={150} className={inputClass} />
          </div>
          <div>
            <label className={labelClass} htmlFor="admin-email">Email</label>
            <input id="admin-email" type="email" value={form.email} onChange={set('email')} required maxLength={255} className={inputClass} />
          </div>
          <div>
            <label className={labelClass} htmlFor="admin-phone">Phone (optional)</label>
            <input id="admin-phone" value={form.phone} onChange={set('phone')} maxLength={20} className={inputClass} />
          </div>
          <div>
            <label className={labelClass} htmlFor="admin-password">
              {isNew ? 'Temporary password' : 'New password (leave blank to keep the current one)'}
            </label>
            <input
              id="admin-password"
              type="password"
              value={form.password}
              onChange={set('password')}
              required={isNew}
              minLength={6}
              autoComplete="new-password"
              className={inputClass}
            />
          </div>
          {formError && <ErrorMessage message={formError} />}
        </form>
      </Modal>

      <Modal
        open={toDelete !== null}
        onClose={() => setToDelete(null)}
        title="Delete admin"
        size="sm"
        busy={deleting}
        footer={
          <>
            <button type="button" className={btnSecondary} onClick={() => setToDelete(null)} disabled={deleting}>
              Cancel
            </button>
            <button type="button" className={btnDanger} onClick={confirmDelete} disabled={deleting}>
              {deleting ? 'Deleting…' : 'Delete'}
            </button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-ink-800">
            <strong>{toDelete?.fullName}</strong> will no longer be able to sign in. Approvals and other records they
            made stay in the system.
          </p>
          {deleteError && <ErrorMessage message={deleteError} />}
        </div>
      </Modal>
    </div>
  );
};

export default AdminsPage;
